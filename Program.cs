/*
===========================================================================
Copyright (C) 2019-2026 Project Crystal Dev Team

This file is part of Project Crystal Server.

Project Crystal Server is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Project Crystal Server is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with Project Crystal Server. If not, see <https://www.gnu.org/licenses/>.
===========================================================================
*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Crystal.FFXILobbyServer
{
    class Program
    {
        public const int PORT_START = 54001;
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        static async Task Main(string[] args)
        {
            // Setup Base DIR
            string baseDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(baseDir);

            // Load NLog Config
            using var nlogStream = typeof(Program).Assembly.GetManifestResourceStream("Crystal.FFXILobbyServer.NLog.config");
            using var nlogReader = System.Xml.XmlReader.Create(nlogStream);
            LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration(nlogReader, null);

            Log.Info("=============================");
            Log.Info("FFXI Lobby Server");
            Log.Info("=============================");

            // Config path arg + load config
            string cfgPath = "./lobby.cfg";
            if (args.Length >= 2 && args[0].Equals("--cfg"))
                cfgPath = args[1];
            FFXILobbyConfig config;
            try
            {
                config = new(cfgPath);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Log.Error($"Could not load the config: {e.Message}");
                Console.ForegroundColor = ConsoleColor.Gray;
                return;
            };

            // Setup POL Database
            if (config.PolDbHost == null ||
                config.PolDbPort == null ||
                config.PolDbName == null ||
                config.PolDbUsername == null ||
                config.PolDbPassword == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Log.Error("FFXIconfig: POL Database was not set.");
                Console.ForegroundColor = ConsoleColor.Gray;
                return;
            }
            Database.POL_DB_HOST = config.PolDbHost;
            Database.POL_DB_PORT = config.PolDbPort;
            Database.POL_DB_NAME = config.PolDbName;
            Database.POL_DB_USERNAME = config.PolDbUsername;
            Database.POL_DB_PASSWORD = config.PolDbPassword;

            // Setup Server
            Server server = new Server(config.WorldList) { Expansions = config.Expansions };
            if (!string.IsNullOrEmpty(config.AccountsListen))
            {
                if (!IPEndPoint.TryParse(config.AccountsListen, out IPEndPoint accountsEndPoint) || accountsEndPoint.Port == 0)
                {
                    Log.Error($"lobby.cfg: <accounts listen=\"{config.AccountsListen}\"/> is not an address and port.");
                    return;
                }
                server.Accounts = new AccountService(accountsEndPoint, config.WorldList);
            }

            // Setup Service
            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Services.AddSystemd();
            builder.Services.AddWindowsService();
            builder.Services.AddSingleton(server);
            builder.Services.AddHostedService<LobbyServerWorker>();
            var host = builder.Build();
            await host.RunAsync();
        }
    }

    public class LobbyServerWorker(Server server) : BackgroundService
    {
        private readonly Server Server = server;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Server.StartServer(54001);
                Server.Accounts?.Start(stoppingToken);
                Console.ForegroundColor = ConsoleColor.Gray;
            }
            catch (ApplicationException e)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Program.Log.Error($"Failed to start server: {e.Message}");
                Console.ForegroundColor = ConsoleColor.Gray;
                throw;
            }
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                Server.Accounts?.Stop();
            }
            catch (Exception ex)
            {
                Program.Log.Debug($"Error stopping server: {ex.Message}");
            }
            await base.StopAsync(cancellationToken);
        }
    }
}
