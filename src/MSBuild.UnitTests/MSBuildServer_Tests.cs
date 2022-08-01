﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.UnitTests;
using Microsoft.Build.UnitTests.Shared;
#if NETFRAMEWORK
using Microsoft.IO;
#else
using System.IO;
#endif
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.Engine.UnitTests
{
    public class SleepingTask : Microsoft.Build.Utilities.Task
    {
        public int SleepTime { get; set; }

        /// <summary>
        /// Sleep for SleepTime milliseconds.
        /// </summary>
        /// <returns>Success on success.</returns>
        public override bool Execute()
        {
            Thread.Sleep(SleepTime);
            return !Log.HasLoggedErrors;
        }
    }

    public class ProcessIdTask : Microsoft.Build.Utilities.Task
    {
        [Output]
        public int Pid { get; set; }

        /// <summary>
        /// Log the id for this process.
        /// </summary>
        /// <returns></returns>
        public override bool Execute()
        {
            Pid = Process.GetCurrentProcess().Id;
            return true;
        }
    }

    public class MSBuildServer_Tests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TestEnvironment _env;
        private static string printPidContents = @$"
<Project>
<UsingTask TaskName=""ProcessIdTask"" AssemblyFile=""{Assembly.GetExecutingAssembly().Location}"" />
    <Target Name='AccessPID'>
        <ProcessIdTask>
            <Output PropertyName=""PID"" TaskParameter=""Pid"" />
        </ProcessIdTask>
        <Message Text=""Server ID is $(PID)"" Importance=""High"" />
    </Target>
</Project>";
        private static string sleepingTaskContents = @$"
<Project>
<UsingTask TaskName=""SleepingTask"" AssemblyFile=""{Assembly.GetExecutingAssembly().Location}"" />
    <Target Name='Sleep'>
        <SleepingTask SleepTime=""100000"" />
    </Target>
</Project>";

        public MSBuildServer_Tests(ITestOutputHelper output)
        {
            _output = output;
            _env = TestEnvironment.Create(_output);
        }

        public void Dispose() => _env.Dispose();

        [Fact]
        public void MSBuildServerTest()
        {
            TransientTestFile project = _env.CreateFile("testProject.proj", printPidContents);
            _env.SetEnvironmentVariable("MSBUILDUSESERVER", "1");
            string output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out bool success, false, _output);
            success.ShouldBeTrue();
            int pidOfInitialProcess = ParseNumber(output, "Process ID is ");
            int pidOfServerProcess = ParseNumber(output, "Server ID is ");
            pidOfInitialProcess.ShouldNotBe(pidOfServerProcess, "We started a server node to execute the target rather than running it in-proc, so its pid should be different.");

            output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out success, false, _output);
            success.ShouldBeTrue();
            int newPidOfInitialProcess = ParseNumber(output, "Process ID is ");
            newPidOfInitialProcess.ShouldNotBe(pidOfServerProcess, "We started a server node to execute the target rather than running it in-proc, so its pid should be different.");
            newPidOfInitialProcess.ShouldNotBe(pidOfInitialProcess, "Process started by two MSBuild executions should be different.");
            pidOfServerProcess.ShouldBe(ParseNumber(output, "Server ID is "), "Node used by both the first and second build should be the same.");

            // Prep to kill the long-lived task we're about to start.
            Task t = Task.Run(() =>
            {
                // Wait for the long-lived task to start
                // If this test seems to fail randomly, increase this time.
                Thread.Sleep(1000);

                // Kill the server
                Process.GetProcessById(pidOfServerProcess).KillTree(1000);
            });

            // Start long-lived task execution
            TransientTestFile sleepProject = _env.CreateFile("napProject.proj", sleepingTaskContents);
            RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, sleepProject.Path, out _);

            t.Wait();

            // Ensure that a new build can still succeed and that its server node is different.
            output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out success, false, _output);

            success.ShouldBeTrue();
            newPidOfInitialProcess = ParseNumber(output, "Process ID is ");
            int newServerProcessId = ParseNumber(output, "Server ID is ");
            // Register process to clean up (be killed) after tests ends.
            _env.WithTransientProcess(newServerProcessId);
            newPidOfInitialProcess.ShouldNotBe(pidOfInitialProcess, "Process started by two MSBuild executions should be different.");
            newPidOfInitialProcess.ShouldNotBe(newServerProcessId, "We started a server node to execute the target rather than running it in-proc, so its pid should be different.");
            pidOfServerProcess.ShouldNotBe(newServerProcessId, "Node used by both the first and second build should not be the same.");
        }

        [Fact]
        public void VerifyMixedLegacyBehavior()
        {
            TransientTestFile project = _env.CreateFile("testProject.proj", printPidContents);
            _env.SetEnvironmentVariable("MSBUILDUSESERVER", "1");

            string output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out bool success, false, _output);
            success.ShouldBeTrue();
            int pidOfInitialProcess = ParseNumber(output, "Process ID is ");
            int pidOfServerProcess = ParseNumber(output, "Server ID is ");
            // Register process to clean up (be killed) after tests ends.
            _env.WithTransientProcess(pidOfServerProcess);
            pidOfInitialProcess.ShouldNotBe(pidOfServerProcess, "We started a server node to execute the target rather than running it in-proc, so its pid should be different.");

            Environment.SetEnvironmentVariable("MSBUILDUSESERVER", "");
            output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out success, false, _output);
            success.ShouldBeTrue();
            pidOfInitialProcess = ParseNumber(output, "Process ID is ");
            int pidOfNewserverProcess = ParseNumber(output, "Server ID is ");
            pidOfInitialProcess.ShouldBe(pidOfNewserverProcess, "We did not start a server node to execute the target, so its pid should be the same.");

            Environment.SetEnvironmentVariable("MSBUILDUSESERVER", "1");
            output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out success, false, _output);
            success.ShouldBeTrue();
            pidOfInitialProcess = ParseNumber(output, "Process ID is ");
            pidOfNewserverProcess = ParseNumber(output, "Server ID is ");
            pidOfInitialProcess.ShouldNotBe(pidOfNewserverProcess, "We started a server node to execute the target rather than running it in-proc, so its pid should be different.");
            pidOfServerProcess.ShouldBe(pidOfNewserverProcess, "Server node should be the same as from earlier.");

            if (pidOfServerProcess != pidOfNewserverProcess)
            {
                // Register process to clean up (be killed) after tests ends.
                _env.WithTransientProcess(pidOfNewserverProcess);
            }
        }

        [Fact]
        public void BuildsWhileBuildIsRunningOnServer()
        {
            _env.SetEnvironmentVariable("MSBUILDUSESERVER", "1");
            TransientTestFile project = _env.CreateFile("testProject.proj", printPidContents);
            TransientTestFile sleepProject = _env.CreateFile("napProject.proj", sleepingTaskContents);

            int pidOfServerProcess;
            Task t;
            // Start a server node and find its PID.
            string output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out bool success, false, _output);
            pidOfServerProcess = ParseNumber(output, "Server ID is ");
            _env.WithTransientProcess(pidOfServerProcess);

            t = Task.Run(() =>
            {
                RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, sleepProject.Path, out _, false, _output);
            });

            // The server will soon be in use; make sure we don't try to use it before that happens.
            Thread.Sleep(1000);

            Environment.SetEnvironmentVariable("MSBUILDUSESERVER", "0");

            output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out success, false, _output);
            success.ShouldBeTrue();
            ParseNumber(output, "Server ID is ").ShouldBe(ParseNumber(output, "Process ID is "), "There should not be a server node for this build.");

            Environment.SetEnvironmentVariable("MSBUILDUSESERVER", "1");

            output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path, out success, false, _output);
            success.ShouldBeTrue();
            pidOfServerProcess.ShouldNotBe(ParseNumber(output, "Server ID is "), "The server should be otherwise occupied.");
            pidOfServerProcess.ShouldNotBe(ParseNumber(output, "Process ID is "), "There should not be a server node for this build.");
            ParseNumber(output, "Server ID is ").ShouldBe(ParseNumber(output, "Process ID is "), "Process ID and Server ID should coincide.");

            // Clean up process and tasks
            // 1st kill registered processes
            _env.Dispose();
            // 2nd wait for sleep task which will ends as soon as the process is killed above.
            t.Wait();
        }

        [Fact]
        public void ServerShouldNotRunWhenNodeReuseEqualsFalse()
        {
            TransientTestFile project = _env.CreateFile("testProject.proj", printPidContents);
            _env.SetEnvironmentVariable("MSBUILDUSESERVER", "1");

            string output = RunnerUtilities.ExecMSBuild(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath, project.Path + " /nodereuse:false", out bool success, false, _output);
            success.ShouldBeTrue();
            int pidOfInitialProcess = ParseNumber(output, "Process ID is ");
            int pidOfServerProcess = ParseNumber(output, "Server ID is ");
            pidOfInitialProcess.ShouldBe(pidOfServerProcess, "We started a server node even when nodereuse is false.");
        }

        private int ParseNumber(string searchString, string toFind)
        {
            Regex regex = new(@$"{toFind}(\d+)");
            Match match = regex.Match(searchString);
            return int.Parse(match.Groups[1].Value);
        }
    }
}
