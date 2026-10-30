// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Configuration;
using Framework.Constants;
using Framework.Database;
using Framework.Networking;
using Game;
using Game.Chat;
using Game.Networking;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace WorldServer
{
    public class Server
    {
        static void Main(string[] args)
        {
            Framework.Logging.ConsoleTimestamps.Enable();

            //Set Culture
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            // --verify-mmap <dataDir> <map> <tileX> <tileY> <x1> <y1> <z1> <x2> <y2> <z2>: load one tile, find a path.
            if (args.Length == 11 && args[0] == "--verify-mmap")
            {
                try
                {
                    float F(int i) => float.Parse(args[i], CultureInfo.InvariantCulture);
                    Console.WriteLine("MMap verified: " + MMapVerification.Verify(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]),
                        new[] { F(5), F(6), F(7) }, new[] { F(8), F(9), F(10) }));
                    Environment.Exit(0);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("MMap verification failed: " + exception.Message);
                    Environment.Exit(1);
                }
            }

            // --build-si-travel-graph <dataDir> <map>: build (or load) and cache one map's SI travel graph.
            if (args.Length == 3 && args[0] == "--build-si-travel-graph")
            {
                Game.AI.SINavigationTool.BuildGraph(args[1], int.Parse(args[2]));
                Environment.Exit(0);
            }

            // --si-walk-random <dataDir> <map> <count>: random connected routes, arrival rate.
            if (args.Length == 4 && args[0] == "--si-walk-random")
            {
                Game.AI.SINavigationTool.RandomWalks(args[1], int.Parse(args[2]), int.Parse(args[3]));
                Environment.Exit(0);
            }

            // --si-walk-replan <dataDir> <map> <count>: random routes replanned mid-leg; counts legs that turn back.
            if (args.Length == 4 && args[0] == "--si-walk-replan")
            {
                Game.AI.SINavigationTool.ReplanWalks(args[1], int.Parse(args[2]), int.Parse(args[3]));
                Environment.Exit(0);
            }

            // --si-walk <dataDir> <map> x1 y1 z1 x2 y2 z2 [verbose]: plan an SI bot's walk leg by leg on the mmaps.
            if ((args.Length == 9 || args.Length == 10) && args[0] == "--si-walk")
            {
                float F(int i) => float.Parse(args[i], CultureInfo.InvariantCulture);
                bool arrived = Game.AI.SINavigationTool.Walk(args[1], int.Parse(args[2]), new(F(3), F(4), F(5)), new(F(6), F(7), F(8)), true, args.Length == 10);
                Environment.Exit(arrived ? 0 : 1);
            }

            // Pure SI controller rule checks; no configuration, database or network.
            if (args.Length == 1 && args[0] == "--verify-si-controller")
            {
                try
                {
                    foreach (string check in Game.AI.SIPlayer.VerifyController())
                        Console.WriteLine("SI controller verified: " + check);
                    Environment.Exit(0);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("SI controller verification failed: " + exception.Message);
                    Environment.Exit(1);
                }
            }

            Console.CancelKeyPress += (o, e) => Global.WorldMgr.StopNow(ShutdownExitCode.Shutdown);

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

            if (!ConfigMgr.Load("Configs/" + Process.GetCurrentProcess().ProcessName + ".conf"))
                ExitNow();

            Log.outHeader(LogFilter.Server, " ");
            Log.outHeader(LogFilter.Server, @"╦ ╦ ╔═╗ ╦═╗ ╦   ╔╦╗   ╔═╗ ╔═╗ ╦═╗ ╦  ╦ ╔═╗ ╦═╗");
            Log.outHeader(LogFilter.Server, @"║║║ ║ ║ ╠╦╝ ║    ║║   ╚═╗ ║╣  ╠╦╝ ╚╗╔╝ ║╣  ╠╦╝");
            Log.outHeader(LogFilter.Server, @"╚╩╝ ╚═╝ ╩╚═ ╩═╝ ═╩╝   ╚═╝ ╚═╝ ╩╚═  ╚╝  ╚═╝ ╩╚═");
            Log.outHeader(LogFilter.Server, " ");

            if (!StartDB())
                ExitNow();

            // Server startup begin
            ServerTime startupBegin = Time.Now.ToServerTime();

            // set server offline (not connectable)
            DB.Login.DirectExecute($"UPDATE realmlist SET flag = (flag & ~{(uint)RealmFlags.VersionMismatch}) | {(uint)RealmFlags.Offline} WHERE id = '{Global.WorldMgr.GetRealm().Id.Index}'");

            Global.RealmMgr.Initialize(ConfigMgr.GetDefaultValue("RealmsStateUpdateDelay", (Seconds)10));

            Global.WorldMgr.SetInitialWorldSettings();

            // Start the Remote Access port (acceptor) if enabled
            if (ConfigMgr.GetDefaultValue("Ra.Enable", false))
            {
                int raPort = ConfigMgr.GetDefaultValue("Ra.Port", 3443);
                string raListener = ConfigMgr.GetDefaultValue("Ra.IP", "0.0.0.0");
                AsyncAcceptor raAcceptor = new();
                if (!raAcceptor.Start(raListener, raPort))
                    Log.outError(LogFilter.Server, "Failed to initialize RemoteAccess Socket Server");
                else
                    raAcceptor.AsyncAccept<RASocket>();
            }

            // Launch the worldserver listener socket
            int worldPort = WorldConfig.Values[WorldCfg.PortWorld].Int32;
            string worldListener = ConfigMgr.GetDefaultValue("BindIP", "0.0.0.0");

            int networkThreads = ConfigMgr.GetDefaultValue("Network.Threads", 1);
            if (networkThreads <= 0)
            {
                Log.outError(LogFilter.Server, "Network.Threads must be greater than 0");
                ExitNow();
                return;
            }

            if (!Global.WorldSocketMgr.StartNetwork(worldListener, worldPort, networkThreads))
            {
                Log.outError(LogFilter.Network, "Failed to start Realm Network");
                ExitNow();
            }

            // set server online (allow connecting now)
            DB.Login.DirectExecute($"UPDATE realmlist SET flag = flag & ~{(uint)RealmFlags.Offline}, population = 0 WHERE id = '{Global.WorldMgr.GetRealm().Id.Index}'");
            Global.WorldMgr.GetRealm().PopulationLevel = 0.0f;
            Global.WorldMgr.GetRealm().Flags = Global.WorldMgr.GetRealm().Flags & ~RealmFlags.VersionMismatch;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            TimeSpan startupDuration = Time.Diff(startupBegin);
            Log.outInfo(LogFilter.Server, $"World initialized in {startupDuration.Minutes} minutes {startupDuration.Seconds} seconds.");

            //- Launch CliRunnable thread
            if (ConfigMgr.GetDefaultValue("Console.Enable", true))
            {
                Thread commandThread = new(CommandManager.InitConsole);
                commandThread.Start();
            }

            WorldUpdateLoop();

            try
            {
                // Shutdown starts here
                Global.WorldMgr.KickAll();                                     // save and kick all players
                Global.WorldMgr.UpdateSessions((Milliseconds)1);               // real players unload required UpdateSessions call

                // unload Battlegroundtemplates before different singletons destroyed
                Global.BattlegroundMgr.DeleteAllBattlegrounds();

                Global.WorldSocketMgr.StopNetwork();
                Game.AI.SIMovement.SIMovementRuntime.Shutdown();

                Global.MapMgr.UnloadAll();                     // unload all grids (including locked in memory)
                Global.TerrainMgr.UnloadAll();
                Global.InstanceLockMgr.Unload();
                Global.ScriptMgr.Unload();

                // set server offline
                DB.Login.DirectExecute($"UPDATE realmlist SET flag = flag | {(uint)RealmFlags.Offline} WHERE id = '{Global.WorldMgr.GetRealm().Id.Index}'");
                Global.RealmMgr.Close();

                ClearOnlineAccounts();

                ExitNow();
            }
            catch (Exception ex)
            {
                Log.outException(ex);
                ExitNow();
            }
        }

        static bool StartDB()
        {
            // Load databases
            DatabaseLoader loader = new();
            loader.AddDatabase(DB.Login, "Login");
            loader.AddDatabase(DB.Characters, "Character");
            loader.AddDatabase(DB.World, "World");
            loader.AddDatabase(DB.Hotfix, "Hotfix");

            if (!loader.Load())
                return false;

            // Get the realm Id from the configuration file
            Global.WorldMgr.GetRealm().Id.Index = ConfigMgr.GetDefaultValue("RealmID", 0);
            if (Global.WorldMgr.GetRealm().Id.Index == 0)
            {
                Log.outError(LogFilter.Server, "Realm ID not defined in configuration file");
                return false;
            }

            Log.outInfo(LogFilter.ServerLoading, $"Realm running as realm ID {Global.WorldMgr.GetRealm().Id.Index} ");

            // Clean the database before starting
            ClearOnlineAccounts();

            Global.WorldMgr.LoadDBVersion();

            Log.outInfo(LogFilter.Server, $"Using World DB: {Global.WorldMgr.GetDBVersion()}");
            return true;
        }

        static void ClearOnlineAccounts()
        {
            // Reset online status for all accounts with characters on the current realm
            DB.Login.DirectExecute($"UPDATE account SET online = 0 WHERE online > 0 AND id IN (SELECT acctid FROM realmcharacters WHERE realmid = {Global.WorldMgr.GetRealm().Id.Index})");

            // Reset online status for all characters
            DB.Characters.DirectExecute("UPDATE characters SET online = 0 WHERE online <> 0");

            // Battlegroundinstance ids reset at server restart
            DB.Characters.DirectExecute("UPDATE character_battleground_data SET instanceId = 0");
        }

        static void WorldUpdateLoop()
        {
            Milliseconds systemLimitSleepTime = (Milliseconds)1; // Thread.Sleep()'s limit

            TimeSpan minUpdateDiff = ConfigMgr.GetDefaultValue("MinWorldUpdateTime", systemLimitSleepTime);
            ServerTime lastUpdateTime = Time.Now.ToServerTime();

            TimeSpan maxCoreStuckTime = ConfigMgr.GetDefaultValue("MaxCoreStuckTime", (Seconds)60);
            TimeSpan halfMaxCoreStuckTime = maxCoreStuckTime / 2;
            if (halfMaxCoreStuckTime == TimeSpan.Zero)
                halfMaxCoreStuckTime = Milliseconds.MaxValue;

            while (!Global.WorldMgr.IsStopped)
            {
                ServerTime currentTime = Time.Now.ToServerTime();

                TimeSpan diff = Time.Diff(lastUpdateTime, currentTime);
                if (diff < minUpdateDiff)
                {
                    TimeSpan sleepTime = minUpdateDiff - diff;
                    if (sleepTime >= halfMaxCoreStuckTime)
                    {
                        Log.outError(LogFilter.Server,
                            $"WorldUpdateLoop() waiting for {(Milliseconds)sleepTime} ms with MaxCoreStuckTime set to {(Milliseconds)maxCoreStuckTime} ms.");
                    }

                    if (sleepTime < systemLimitSleepTime)
                    {
                        sleepTime = systemLimitSleepTime;
                    }

                    // sleep until enough time passes that we can update all timers
                    Thread.Sleep(sleepTime);
                    continue;
                }

                ///- Update the loop time
                LoopTime.Update(currentTime);
                Global.WorldMgr.UpdateShutDownMessage(currentTime);
                Global.WorldMgr.Update(diff);
                lastUpdateTime = currentTime;
            }
        }

        static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Log.outException(ex);
            Log.FlushConsole();
        }

        static void ExitNow()
        {
            Log.outInfo(LogFilter.Server, "Halting process...");
            Thread.Sleep(5000);
            Environment.Exit(Global.WorldMgr.GetExitCode());
        }
    }
}


