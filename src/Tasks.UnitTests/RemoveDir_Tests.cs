// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Tasks;
using Microsoft.Build.Utilities;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.UnitTests
{
    sealed public class RemoveDir_Tests
    {
        ITestOutputHelper _output;
        public RemoveDir_Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        /*
         * Method:   AttributeForwarding
         *
         * Make sure that attributes set on input items are forwarded to output items.
         */
        [Fact]
        public void AttributeForwarding()
        {
            RemoveDir t = new RemoveDir();

            ITaskItem i = new TaskItem("MyNonExistentDirectory");
            i.SetMetadata("Locale", "en-GB");
            t.Directories = new ITaskItem[] { i };
            t.BuildEngine = new MockEngine(_output);

            t.Execute();

            t.RemovedDirectories[0].GetMetadata("Locale").ShouldBe("en-GB");
            t.RemovedDirectories[0].ItemSpec.ShouldBe("MyNonExistentDirectory");
            Directory.Exists(t.RemovedDirectories[0].ItemSpec).ShouldBeFalse();
        }

        [Fact]
        public void SimpleDelete()
        {

            using (TestEnvironment env = TestEnvironment.Create(_output))
            {
                List<TaskItem> list = new List<TaskItem>();

                for (int i = 0; i < 20; i++)
                {
                    list.Add(new TaskItem(env.CreateFolder().Path));
                }

                RemoveDir t = new RemoveDir();

                t.Directories = list.ToArray();
                t.BuildEngine = new MockEngine(_output);

                t.Execute().ShouldBeTrue();

                t.RemovedDirectories.Length.ShouldBe(list.Count);

                for (int i = 0; i < 20; i++)
                {
                    Directory.Exists(list[i].ItemSpec).ShouldBeFalse();
                }
            }
        }
    }
}



