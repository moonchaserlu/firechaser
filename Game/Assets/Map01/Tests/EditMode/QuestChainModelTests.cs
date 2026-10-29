using System.Collections.Generic;
using NUnit.Framework;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>任务链纯规则：顺序推进、提前获得的事实在开启时认可、重复事实不重复完成。</summary>
    public sealed class QuestChainModelTests
    {
        private static QuestChainModel Chain()
        {
            return new QuestChainModel(new List<(string, string)> { ("Q01", "a"), ("Q02", "b"), ("Q03", "c") });
        }

        [Test]
        public void Begin_StartsFirstQuest_OthersNotStarted()
        {
            var chain = Chain();
            Assert.IsFalse(chain.IsStarted);
            chain.Begin();
            Assert.AreEqual("Q01", chain.CurrentId);
            Assert.AreEqual(QuestStatus.Active, chain.StatusOf("Q01"));
            Assert.AreEqual(QuestStatus.NotStarted, chain.StatusOf("Q02"));
            chain.Begin();
            Assert.AreEqual("Q01", chain.CurrentId, "重复开始无效");
        }

        [Test]
        public void Facts_CompleteQuestsInOrder()
        {
            var chain = Chain();
            var completed = new List<int>();
            chain.QuestCompleted += completed.Add;
            chain.Begin();
            chain.SetFact("a");
            Assert.AreEqual("Q02", chain.CurrentId);
            Assert.AreEqual(QuestStatus.Completed, chain.StatusOf("Q01"));
            chain.SetFact("b");
            chain.SetFact("c");
            Assert.IsTrue(chain.IsFinished);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, completed);
        }

        [Test]
        public void EarlyFact_IsRecognizedWhenQuestStarts_WithoutSkippingAhead()
        {
            var chain = Chain();
            chain.Begin();
            Assert.IsTrue(chain.SetFact("c"), "提前获得的事实要记下");
            Assert.AreEqual("Q01", chain.CurrentId, "后面的事实不能让前面的任务跳过");
            Assert.AreEqual(QuestStatus.NotStarted, chain.StatusOf("Q03"));
            chain.SetFact("a");
            chain.SetFact("b");
            Assert.IsTrue(chain.IsFinished, "Q03 开启时立即认可已有事实");
            Assert.AreEqual(3, chain.CompletedCount);
        }

        [Test]
        public void DuplicateFact_DoesNotCompleteTwice()
        {
            var chain = Chain();
            var completions = 0;
            var finished = 0;
            chain.QuestCompleted += _ => completions++;
            chain.ChainCompleted += () => finished++;
            chain.Begin();
            Assert.IsTrue(chain.SetFact("a"));
            Assert.IsFalse(chain.SetFact("a"), "同一事实只记一次");
            chain.SetFact("b");
            chain.SetFact("c");
            chain.SetFact("c");
            Assert.AreEqual(3, completions);
            Assert.AreEqual(1, finished);
        }

        [Test]
        public void FactsBeforeBegin_AreKept()
        {
            var chain = Chain();
            chain.SetFact("a");
            chain.SetFact("b");
            chain.Begin();
            Assert.AreEqual("Q03", chain.CurrentId);
        }
    }
}
