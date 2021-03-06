﻿using System;
using System.IO;
using System.Linq;
using Cachalot.Linq;
using Client;
using Client.Interface;
using NUnit.Framework;
using Server.Persistence;
using UnitTests.TestData;

namespace UnitTests
{
    [TestFixture]
    public class TestFixtureAdminInterface
    {
        [OneTimeSetUp]
        public void RunBeforeAnyTests()
        {
            Environment.CurrentDirectory = TestContext.CurrentContext.TestDirectory;
            Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
        }

        [SetUp]
        public void SetUp()
        {
            if (Directory.Exists(Constants.DataPath))
            {
                Directory.Delete(Constants.DataPath, true);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // deactivate all failure simulations
            Dbg.DeactivateSimulation();
        }

        [Test]
        public void Dump_all_data_and_restore_from_dump()
        {
            ClientConfig config = new ClientConfig();
            config.LoadFromFile("inprocess_persistent_config.xml");

            const string dumpPath = "dump";

            if (Directory.Exists(dumpPath))
            {
                Directory.Delete(dumpPath, true);
            }

            Directory.CreateDirectory(dumpPath);

            int maxId1;
            int maxId2;
            using (var connector = new Connector(config))
            {
                DataSource<Trade> dataSource = connector.DataSource<Trade>();


                for (int i = 0; i < 1010; i++)
                {
                    if (i % 10 == 0)
                    {
                        dataSource.Put(new Trade(i, 1000 + i, "TOTO", DateTime.Now.Date, 150));
                    }
                    else
                    {
                        dataSource.Put(new Trade(i, 1000 + i, "TATA", DateTime.Now.Date, 150));
                    }
                }


                DataAdmin admin = connector.AdminInterface();


                // generate unique ids before dump
                maxId1 = connector.GenerateUniqueIds("blahblah", 20).Max();
                maxId2 = connector.GenerateUniqueIds("foobar", 20).Max();

                admin.Dump(dumpPath);


                Assert.IsTrue(Directory.Exists(dumpPath));
                var todayDumpPath = Path.Combine(dumpPath, DateTime.Today.ToString("yyyy-MM-dd"));

                Assert.IsTrue(Directory.Exists(todayDumpPath),
                    $"not found {todayDumpPath} current directory = {Directory.GetCurrentDirectory()}");
                var files = Directory.EnumerateFiles(todayDumpPath).ToList();


                Assert.IsTrue(files.Any(f => f.Contains("schema.json")), "schema.json was not stored in the dump");
                Assert.IsTrue(files.Any(f => f.Contains("sequence")), "sequences where not stored in the dump");

                var dataFiles = files.Where(f => !f.Contains("schema.json")).ToList();
                Assert.AreEqual(3, dataFiles.Count); // one containing 1000 the second one 10 and the sequence file

                // add some data after dump
                dataSource.Put(new Trade(2000, 3000, "TITI", DateTime.Now.Date, 150));
            }


            // first import a dump in a non empty database 
            using (var connector = new Connector(config))
            {
                DataAdmin admin = connector.AdminInterface();

                admin.ImportDump(dumpPath);


                // generate unique ids after dump and check that they are higher than the one generated before dump
                // meanig the unique id generators (sequences)  have been restored
                var minId1 = connector.GenerateUniqueIds("blahblah", 20).Max();
                var minId2 = connector.GenerateUniqueIds("foobar", 20).Max();

                Assert.Greater(minId1, maxId1, "the sequences ware not correctly retored from dump");
                Assert.Greater(minId2, maxId2, "the sequences ware not correctly retored from dump");
                

                DataSource<Trade> dataSource = connector.DataSource<Trade>();
                var folders = new[] {"TATA", "TOTO"};


                var list = dataSource.Where(t => folders.Contains(t.Folder)).ToList();

                Assert.AreEqual(1010, list.Count);

                var count = dataSource.Count(t => t.Folder == "TITI");
                Assert.AreEqual(0, count, "this object should not be found as it was added after dump");

                dataSource.DeleteMany(t => t.Folder == "TATA");

                count = dataSource.Count(t => t.Folder == "TATA");
                Assert.AreEqual(0, count);

                count = dataSource.Count(t => t.Folder == "TOTO");
                Assert.IsTrue(count > 0 && count < 1000, "count > 0 && count < 1000");

                admin.Dump(dumpPath);

                // less than 1000 items. The dump should now contain one single data file
                Assert.IsTrue(Directory.Exists(dumpPath));

                var todayDumpPath = Path.Combine(dumpPath, DateTime.Today.ToString("yyyy-MM-dd"));

                Assert.IsTrue(Directory.Exists(todayDumpPath),
                    $"not found {todayDumpPath} current directory = {Directory.GetCurrentDirectory()}");

                var files = Directory.EnumerateFiles(todayDumpPath).ToList();


                Assert.IsTrue(files.Any(f => f.Contains("schema.json")), "schema.json was not stored in the dump");

                var dataFiles = files.Where(f => !f.Contains("schema.json") && !f.Contains("sequence")).ToList();
                Assert.AreEqual(1, dataFiles.Count);
            }

            // reload and check your data is still there
            using (var connector = new Connector(config))
            {
                DataSource<Trade> dataSource = connector.DataSource<Trade>();

                var folders = new[] {"TATA", "TOTO"};

                var list = dataSource.Where(t => folders.Contains(t.Folder)).ToList();

                Assert.IsTrue(list.Count > 0, "list.Count > 0");
                Assert.IsTrue(list.All(t => t.Folder == "TOTO"), "list.All(t=>t.Folder == 'TOTO')");
            }

            // import a dump into an empty database            
            Directory.Delete(Constants.DataPath, true);

            using (var connector = new Connector(config))
            {
                DataAdmin admin = connector.AdminInterface();

                admin.ImportDump(dumpPath);


                DataSource<Trade> dataSource = connector.DataSource<Trade>();

                var folders = new[] {"TATA", "TOTO"};

                var list = dataSource.Where(t => folders.Contains(t.Folder)).ToList();

                Assert.IsTrue(list.Count > 0, "list.Count > 0");
                Assert.IsTrue(list.All(t => t.Folder == "TOTO"), "list.All(t=>t.Folder == 'TOTO')");
            }

            // reinitialize from dump
            Directory.Delete(Constants.DataPath, true);

            using (var connector = new Connector(config))
            {
                DataAdmin admin = connector.AdminInterface();

                admin.InitializeFromDump(dumpPath);


                DataSource<Trade> dataSource = connector.DataSource<Trade>();

                var folders = new[] { "TATA", "TOTO" };

                var list = dataSource.Where(t => folders.Contains(t.Folder)).ToList();

                Assert.IsTrue(list.Count > 0, "list.Count > 0");
                Assert.IsTrue(list.All(t => t.Folder == "TOTO"), "list.All(t=>t.Folder == 'TOTO')");
            }
        }

        /// <summary>
        /// Can not be run in release mode as failure simulations are available only in debug mode
        /// </summary>
        [Test]        
        public void If_dump_import_fails_rollback_and_check_no_data_was_lost()
        {
#if DEBUG

            ClientConfig config = new ClientConfig();
            config.LoadFromFile("inprocess_persistent_config.xml");

            const string dumpPath = "dump";

            if (Directory.Exists(dumpPath))
            {
                Directory.Delete(dumpPath, true);
            }

            Directory.CreateDirectory(dumpPath);

            using (var connector = new Connector(config))
            {
                DataSource<Trade> dataSource = connector.DataSource<Trade>();


                for (int i = 0; i < 1010; i++)
                {
                    if (i % 10 == 0)
                    {
                        dataSource.Put(new Trade(i, 1000 + i, "TOTO", DateTime.Now.Date, 150));
                    }
                    else
                    {
                        dataSource.Put(new Trade(i, 1000 + i, "TATA", DateTime.Now.Date, 150));
                    }
                }


                DataAdmin admin = connector.AdminInterface();

                admin.Dump(dumpPath);

                // add some data after dump
                dataSource.Put(new Trade(2000, 3000, "TITI", DateTime.Now.Date, 150));

            }


            // simulate exception during dump import
            using (var connector = new Connector(config))
            {


                DataAdmin admin = connector.AdminInterface();
                
                Dbg.ActivateSimulation(100);


                Assert.Throws<CacheException>(()=> admin.ImportDump(dumpPath));


                DataSource<Trade> dataSource = connector.DataSource<Trade>();
                var folders = new[] {"TATA", "TOTO"};


                var list = dataSource.Where(t => folders.Contains(t.Folder)).ToList();

                Assert.AreEqual(1010, list.Count);

                dataSource.DeleteMany(t => t.Folder == "TATA");

                var count = dataSource.Count(t => t.Folder == "TATA");
                Assert.AreEqual(0, count);

                count = dataSource.Count(t => t.Folder == "TITI");
                Assert.AreEqual(1, count, "this object should exist as the dump import failed");

               
            }

#endif
        }
    }
}