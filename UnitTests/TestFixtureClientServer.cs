﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Channel;
using Client.Core;
using Client.Interface;
using Client.Messages;
using Client.Queries;
using NUnit.Framework;
using UnitTests.TestData;
using ServerConfig = Server.ServerConfig;

#endregion

namespace UnitTests
{
    [TestFixture]
    public class TestFixtureClientServer
    {
        [SetUp]
        public void Init()
        {
            _client = new CacheClient();
            var channel = new InProcessChannel();
            _client.Channel = channel;


            _server = new Server.Server(new ServerConfig()) {Channel = channel};
            _server.Start();

            _client.RegisterTypeIfNeeded(typeof(CacheableTypeOk));

            var cfg = new ClientConfig();
            cfg.LoadFromFile("CacheClientConfig.xml");

            foreach (var description in cfg.TypeDescriptions)
            {
                if (description.Value.FullTypeName == "UnitTests.TestData.Trade")
                {
                    _client.RegisterTypeIfNeeded(typeof(Trade), description.Value);
                }
            }

        }

        [TearDown]
        public void Exit()
        {
            _client.Dispose();
            _server.Stop();
        }

        private CacheClient _client;

        private Server.Server _server;


        [OneTimeSetUp]
        public void RunBeforeAnyTests()
        {
            Environment.CurrentDirectory = TestContext.CurrentContext.TestDirectory;
            Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
        }


        private Semaphore _requestsFinished;

        [Test]
        public void DataAccess()
        {
            //add two new items
            var item1 = new CacheableTypeOk(1, 1001, "aaa", new DateTime(2010, 10, 10), 1500);
            _client.Put(item1);

            var item2 = new CacheableTypeOk(2, 1002, "aaa", new DateTime(2010, 10, 10), 1600);
            _client.Put(item2);

            //reload the first one by primary key
            var item1Reloaded = _client.GetOne<CacheableTypeOk>(1);
            Assert.AreEqual(item1, item1Reloaded);

            //try to load nonexistent object by primary key
            var itemNull = _client.GetOne<CacheableTypeOk>("UniqueKey", 2055);
            Assert.IsNull(itemNull);

            //try to load nonexistent object by unique key
            itemNull = _client.GetOne<CacheableTypeOk>(55);
            Assert.IsNull(itemNull);

            //reload both items by folder name
            IList<CacheableTypeOk> itemsInAaa = _client.GetMany<CacheableTypeOk>("IndexKeyFolder == aaa").ToList();
            Assert.AreEqual(itemsInAaa.Count, 2);

            //change the folder of the first item and put it back into the cache
            item1.IndexKeyFolder = "bbb";
            _client.Put(item1);

            //now it should be only one item left in aaa
            itemsInAaa = _client.GetMany<CacheableTypeOk>("IndexKeyFolder == aaa").ToList();
            Assert.AreEqual(itemsInAaa.Count, 1);

            //get both of them again by date
            IList<CacheableTypeOk> allItems = _client.GetMany<CacheableTypeOk>($"IndexKeyDate == {new DateTime(2010, 10, 10).Ticks}").ToList();
            Assert.AreEqual(allItems.Count, 2);

            var desc = ClientSideTypeDescription.RegisterType<CacheableTypeOk>().AsTypeDescription;

            //get object descriptions 
            var qb = new QueryBuilder(typeof(CacheableTypeOk));
            
            var itemDescriptions = _client.GetObjectDescriptions(qb.GetManyWhere($"IndexKeyDate == {new DateTime(2010, 10, 10).Ticks}"));
            Assert.AreEqual(itemDescriptions.Count, 2);
            
            Assert.AreEqual(itemDescriptions[0].PrimaryKey, desc.MakePrimaryKeyValue(1));
            
            Assert.AreEqual(itemDescriptions[1].PrimaryKey, desc.MakePrimaryKeyValue(2));


            //get both of them using an In query
            var builder = new QueryBuilder(typeof(CacheableTypeOk));
            var q = builder.In("indexkeyfolder", "aaa", "bbb");
            allItems = _client.GetMany<CacheableTypeOk>(q).ToList();
            Assert.AreEqual(allItems.Count, 2);

            //remove the first one
            _client.Remove<CacheableTypeOk>(1);

            //removing non existent item should throw exception
            Assert.Throws<CacheException>(() => _client.Remove<CacheableTypeOk>(46546));
            

            //the previous query should now return only one item
            allItems = _client.GetMany<CacheableTypeOk>(q).ToList();
            Assert.AreEqual(allItems.Count, 1);

            //COUNT should also return 1
            int count = _client.EvalQuery(q).Value;
            Assert.AreEqual(count, 1);
        }

        [Test]
        public void DomainDescription()
        {
            var builder = new QueryBuilder(typeof(CacheableTypeOk));
            var evalResult = _client.EvalQuery(builder.GetMany("IndexKeyFolder=aaa"));
            Assert.IsFalse(evalResult.Key);
            Assert.AreEqual(evalResult.Value, 0);

            var item1 = new CacheableTypeOk(1, 1001, "aaa", new DateTime(2010, 10, 10), 1500);
            _client.Put(item1);

            var item2 = new CacheableTypeOk(2, 1002, "aaa", new DateTime(2010, 10, 10), 1600);
            _client.Put(item2);

            evalResult = _client.EvalQuery(builder.GetMany("IndexKeyFolder=aaa"));
            Assert.IsFalse(evalResult.Key);
            Assert.AreEqual(evalResult.Value, 2);

            var description = new DomainDescription(typeof(CacheableTypeOk)) {IsFullyLoaded = true};

            //describe the domain as complete
            _client.DeclareDomain(description, DomainDeclarationAction.Set);
            evalResult = _client.EvalQuery(builder.GetMany("IndexKeyFolder=aaa"));
            Assert.IsTrue(evalResult.Key);
            Assert.AreEqual(evalResult.Value, 2);

            //remove the completeness declaration
            _client.DeclareDomain(description, DomainDeclarationAction.Remove);
            evalResult = _client.EvalQuery(builder.GetMany("IndexKeyFolder=aaa"));
            Assert.IsFalse(evalResult.Key);

            description.AddOrReplace(builder.MakeAtomicQuery("IndexKeyFolder", "aaa"));
            _client.DeclareDomain(description, DomainDeclarationAction.Add);
            evalResult = _client.EvalQuery(builder.GetMany("IndexKeyFolder=aaa"));
            Assert.IsTrue(evalResult.Key);

            _client.DeclareDomain(description, DomainDeclarationAction.Remove);
            evalResult = _client.EvalQuery(builder.GetMany("IndexKeyFolder=aaa"));
            Assert.IsFalse(evalResult.Key);
        }

        [Test]
        public void GetServerInfo()
        {
            var types = _client.GetKnownTypes();
            Assert.AreEqual(types.Count, 2);

            var desc = _client.GetServerDescription();
            Assert.AreEqual(desc.DataStoreInfoByFullName.Count, 2);
        }

        

        [Test]
        public void MultiThreadedDataAccess()
        {
            //first load data 
            for (int i = 0; i < 1000; i++)
            {
                var item = new CacheableTypeOk(i, 10000 + i, "aaa", new DateTime(2010, 10, 10), 1500 + i);
                _client.Put(item);
            }


            _requestsFinished = new Semaphore(0, 100);

            // 100 parallel requests
            for (int i = 0; i < 100; i++)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    //reload both items by folder name
                    IList<CacheableTypeOk> items =
                        _client.GetMany<CacheableTypeOk>("IndexKeyValue < 1700")
                            .ToList();
                    Assert.AreEqual(items.Count, 200);

                    _requestsFinished.Release();
                });
            }


            for (int i = 0; i < 100; i++)
            {
                _requestsFinished.WaitOne();
            }
        }


        [Test]
        public void SearchByIndexedList()
        {
            //add two new items
            var trade1 = new Trade(1, 1001, "aaa", new DateTime(2010, 10, 10), 1500) {Accounts = {1, 101, 10001, 7}};
            _client.Put(trade1);

            var trade2 = new Trade(2, 1002, "bbbb", new DateTime(2010, 10, 10), 1600) {Accounts = {2, 102, 10002, 7}};
            _client.Put(trade2);

            var tradeDescription = _client.KnownTypes["UnitTests.TestData.Trade"];

            var builder = new QueryBuilder(tradeDescription.AsTypeDescription);

            {
                var q = builder.Contains("ACCOUNTS", 101);
                var all = _client.GetMany<Trade>(q).ToList();


                Assert.AreEqual(all.Count, 1);
            }


            {
                var q = builder.Contains("ACCOUNTS", 7);
                var all = _client.GetMany<Trade>(q).ToList();


                Assert.AreEqual(all.Count, 2);
            }

            {
                var q = builder.Contains("ACCOUNTS", 101, 102);
                var all = _client.GetMany<Trade>(q).ToList();


                Assert.AreEqual(all.Count, 2);
            }
        }

        [Test]
        public void StreamAvailableObjects()
        {
            //add two new items
            var item1 = new CacheableTypeOk(1, 1001, "aaa", new DateTime(2010, 10, 10), 1500);
            _client.Put(item1);

            var item2 = new CacheableTypeOk(2, 1002, "aaa", new DateTime(2010, 10, 10), 1600);
            _client.Put(item2);

            //ask for items 1 2 3 4 (1 2 should be returned and 3 4 not found)
            var items = new List<KeyValue>
            {
                new KeyValue(1,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey")),
                new KeyValue(2,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey")),
                new KeyValue(3,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey")),
                new KeyValue(4,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey"))
            };

            var wait = new ManualResetEvent(false);
            var found = new List<CacheableTypeOk>();
            var notFound = _client.GetAvailableItems(items,
                delegate(CacheableTypeOk item, int currentItem, int totalItems)
                {
                    found.Add(item);
                    if (currentItem == totalItems)
                        wait.Set();
                }, delegate { });

            wait.WaitOne();
            Assert.AreEqual(notFound.Count, 2);
            Assert.AreEqual(notFound[0], 3);
            Assert.AreEqual(notFound[1], 4);
            Assert.AreEqual(found.Count, 2);
            Assert.AreEqual(found[0].PrimaryKey, 1);
            Assert.AreEqual(found[1].PrimaryKey, 2);
        }

        [Test]
        public void StreamAvailableObjectsWithCriteria()
        {
            //add two new items
            var item1 = new CacheableTypeOk(1, 1001, "aaa", new DateTime(2010, 10, 10), 1500);
            _client.Put(item1);

            var item2 = new CacheableTypeOk(2, 1002, "aaa", new DateTime(2010, 10, 10), 1600);
            _client.Put(item2);

            var item3 = new CacheableTypeOk(3, 1003, "bbb", new DateTime(2010, 10, 10), 1600);
            _client.Put(item3);

            //ask for items 1 2 3 4 (1 2 should be returned and 3 4 not found)
            var items = new List<KeyValue>
            {
                new KeyValue(1,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey")),
                new KeyValue(2,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey")),
                new KeyValue(3,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey")),
                new KeyValue(4,
                    new KeyInfo(KeyDataType.IntKey, KeyType.Primary,
                        "PrimaryKey"))
            };

            var builder = new QueryBuilder(typeof(CacheableTypeOk));
            Query folderIsAaa = builder.MakeAtomicQuery("IndexKeyFolder", "aaa");

            var wait = new ManualResetEvent(false);
            var found = new List<CacheableTypeOk>();
            var notFound = _client.GetAvailableItems(items, folderIsAaa,
                delegate(CacheableTypeOk item, int currentItem, int totalItems)
                {
                    found.Add(item);
                    if (currentItem == totalItems)
                        wait.Set();
                }, delegate { });

            wait.WaitOne();
            Assert.AreEqual(notFound.Count, 2);
            Assert.AreEqual(notFound[0], 3);
            Assert.AreEqual(notFound[1], 4);
            Assert.AreEqual(found.Count, 2);
            Assert.AreEqual(found[0].PrimaryKey, 1);
            Assert.AreEqual(found[1].PrimaryKey, 2);
        }

        [Test]
        public void Truncate()
        {
            //add two new items
            var item1 = new CacheableTypeOk(1, 1001, "aaa", new DateTime(2010, 10, 10), 1500);
            _client.Put(item1);

            var item2 = new CacheableTypeOk(2, 1002, "aaa", new DateTime(2010, 10, 10), 1600);
            _client.Put(item2);

            IList<CacheableTypeOk> itemsInAaa = _client.GetMany<CacheableTypeOk>("IndexKeyFolder == aaa").ToList();
            Assert.AreEqual(itemsInAaa.Count, 2);

            var desc = _client.GetServerDescription();
            long hits = desc.DataStoreInfoByFullName[typeof(CacheableTypeOk).FullName].HitCount;

            Assert.AreEqual(hits, 1);

            _client.Truncate<CacheableTypeOk>();

            desc = _client.GetServerDescription();
            hits = desc.DataStoreInfoByFullName[typeof(CacheableTypeOk).FullName].HitCount;
            long count = desc.DataStoreInfoByFullName[typeof(CacheableTypeOk).FullName].Count;

            Assert.AreEqual(hits, 0);
            Assert.AreEqual(count, 0);
        }
    }
}