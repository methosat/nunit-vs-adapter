// ****************************************************************
// Copyright (c) 2011 NUnit Software. All rights reserved.
// ****************************************************************
 //#define LAUNCHDEBUGGER
using System.Collections.Generic;
#if LAUNCHDEBUGGER
using System.Diagnostics;
#endif
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NUnit.Core;
using NUnit.Util;

namespace NUnit.VisualStudio.TestAdapter
{
    using System;
    using Nett;
    [FileExtension(".dll")]
    [FileExtension(".exe")]
    [DefaultExecutorUri(NUnitTestExecutor.ExecutorUri)]
    public sealed class NUnitTestDiscoverer : NUnitTestAdapter, ITestDiscoverer
    {

        #region ITestDiscoverer Members

        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger messageLogger, ITestCaseDiscoverySink discoverySink)
        {
            TestLog.Initialize(messageLogger);
            if (RegistryFailure)
            {
                TestLog.SendErrorMessage(ErrorMsg);
            }
            Info("discovering tests", "started");

            // Ensure any channels registered by other adapters are unregistered
            CleanUpRegisteredChannels();

            // var initPath = Path.Combine(Environment.CurrentDirectory, "init.tml");

            var initPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NUnitTestAdapter");
            initPath = Path.Combine(initPath, "init.tml");

            var blackList = new List<string>();
            if (File.Exists(initPath))
            {
                var config = Toml.ReadFile<Configuration>(initPath);
                blackList = config.Blacklist.Select(b => b.ToLowerInvariant()).ToList();
            }
           

            foreach (string sourceAssembly in sources)
            {
                if (blackList.Contains(Path.GetFileName(sourceAssembly.ToLowerInvariant())))
                {
                    TestLog.SendDebugMessage("Ignore " + sourceAssembly);
                    continue;
                }

                TestLog.SendDebugMessage("Processing " + sourceAssembly);

                //config.Blacklist.Add(sourceAssembly);

                TestRunner runner = new TestDomain();
                var package = CreateTestPackage(sourceAssembly);
                TestConverter testConverter = null;
                try
                {
                    if (runner.Load(package))
                    {
                        testConverter = new TestConverter(TestLog, sourceAssembly);
                        int cases = ProcessTestCases(runner.Test, discoverySink, testConverter);
                        TestLog.SendDebugMessage(string.Format("Discovered {0} test cases", cases));
                    }
                    else
                    {
                        TestLog.NUnitLoadError(sourceAssembly);
                    }
                }
                catch (BadImageFormatException)
                {
                    // we skip the native c++ binaries that we don't support.
                    TestLog.AssemblyNotSupportedWarning(sourceAssembly);
                }

                catch (FileNotFoundException ex)
                {
                    // Probably from the GetExportedTypes in NUnit.core, attempting to find an assembly, not a problem if it is not NUnit here
                    TestLog.DependentAssemblyNotFoundWarning(ex.FileName, sourceAssembly);
                }
                catch (FileLoadException ex)
                {
                    // Attempts to load an invalid assembly, or an assembly with missing dependencies
                    TestLog.LoadingAssemblyFailedWarning(ex.FileName, sourceAssembly);
                }
                catch (UnsupportedFrameworkException ex)
                {
                    TestLog.UnsupportedFrameworkWarning(sourceAssembly);
                }
                catch (Exception ex)
                {

                    TestLog.SendErrorMessage("Exception thrown discovering tests in " + sourceAssembly, ex);
                }
                finally
                {
                    if (testConverter != null)
                        testConverter.Dispose();
                    runner.Unload();
                }
            }

            //Toml.WriteFile(config, @"test.tml");

            Info("discovering test", "finished");
        }

        private int ProcessTestCases(ITest test, ITestCaseDiscoverySink discoverySink, TestConverter testConverter)
        {
            int cases = 0;

            if (test.IsSuite)
            {
                cases += test.Tests.Cast<ITest>().Sum(child => ProcessTestCases(child, discoverySink, testConverter));
            }
            else
            {
                try
                {
#if LAUNCHDEBUGGER
            Debugger.Launch();
#endif
                    TestCase testCase = testConverter.ConvertTestCase(test);

                    discoverySink.SendTestCase(testCase);
                    cases += 1;
                }
                catch (Exception ex)
                {
                    TestLog.SendErrorMessage("Exception converting " + test.TestName.FullName, ex);
                }
            }

            return cases;
        }

        #endregion


    }
}
