using System.Collections.Generic;
using NUnit.Framework;

namespace WildernessExtraction.Tutorial.Tests
{
    public sealed class TutorialStepModelTests
    {
        private static TutorialStepModel Create()
        {
            return new TutorialStepModel(new List<(string, string[])>
            {
                ("move", new[] { "moved", "sprinted" }),
                ("loot", new[] { "crate_a", "crate_b", "backpack_opened" }),
                ("home", new[] { "exit_held" })
            });
        }

        [Test]
        public void Step_CompletesOnlyWhenAllFactsArrive()
        {
            var model = Create();
            var completed = new List<int>();
            model.StepCompleted += completed.Add;

            Assert.IsFalse(model.Report("moved"));
            Assert.AreEqual("move", model.CurrentId);
            Assert.AreEqual((1, 2), model.Progress());
            Assert.IsTrue(model.Report("sprinted"));
            Assert.AreEqual("loot", model.CurrentId);
            CollectionAssert.AreEqual(new[] { 0 }, completed);
        }

        [Test]
        public void EarlyFacts_CountWhenTheirStepArrives_AndCanChainSteps()
        {
            var model = Create();
            model.Report("backpack_opened");
            model.Report("crate_a");
            model.Report("crate_b");
            Assert.AreEqual("move", model.CurrentId, "顺序不变：没学会走路前不会跳段");

            model.Report("moved");
            model.Report("sprinted");
            Assert.AreEqual("home", model.CurrentId, "提前到齐的事实让后一段一起完成");
        }

        [Test]
        public void DuplicateFacts_AreIgnored_AndFinishFiresOnce()
        {
            var model = Create();
            var finished = 0;
            model.Finished += () => finished++;
            foreach (var fact in new[] { "moved", "sprinted", "crate_a", "crate_b", "backpack_opened", "exit_held" })
            {
                model.Report(fact);
            }

            Assert.IsTrue(model.IsFinished);
            Assert.IsFalse(model.Report("exit_held"));
            model.Skip();
            Assert.AreEqual(1, finished);
            Assert.IsNull(model.CurrentId);
        }

        [Test]
        public void Skip_FinishesWithoutFacts()
        {
            var model = Create();
            var finished = false;
            model.Finished += () => finished = true;
            model.Skip();
            Assert.IsTrue(finished);
            Assert.IsTrue(model.IsFinished);
            Assert.IsFalse(model.HasFact("exit_held"), "跳过不伪造“走完”的事实");
        }
    }
}
