using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace dev.jamac.AtmosphericDragConfigurable
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class AtmosphericDrag : MySessionComponentBase
    {
        public static AtmosphericDrag Instance; // the only way to access session comp from other classes and the only accepted static.

        // Planets Detection
        public static Dictionary<long, MyPlanet> planets = new Dictionary<long, MyPlanet>();
        public static List<long> removePlanets = new List<long>();
        public const float MIN_ATMOSPHERE = 0.0f;
        public const float MAX_ATMOSPHERE = 0.85f; // 0.82

        // Entity Lists
        public static HashSet<IMyEntity> entityList = new HashSet<IMyEntity>();
        public static HashSet<IMyEntity> gridList = new HashSet<IMyEntity>();
        public Dictionary<long, Grid> grids = new Dictionary<long, Grid>();

        // Timer Ticks
        public const int SKIP_TICKS_2 = 2; // 20 times per second
        public const int SKIP_TICKS_60 = 60;  // Once per second
        public const int SKIP_TICKS_180 = 180; // Once per 3 seconds
        public const int SKIP_TICKS_300 = 300; // Once per 5 seconds
        public const int SKIP_TICKS_600 = 600; // Once per 10 seconds
        public const int SKIP_TICKS_3600 = 3600; // Once per 1 minute

        // Other Constants
        public const bool DEBUG = false;
        public const string CONFIG_FILE = "AtmosphericDragConfigurable.cfg";
        public const float DRAG_MULTIPLIER_INTERNAL = 0.5f;
        public const ushort HANDLER_ID_SET = 38010;
        public const ushort HANDLER_ID_GET = 38011;
        public const ushort HANDLER_ID_RESPOND = 38012;

        // Other Variables
        internal bool initalized = false;
        internal bool isServer = false;
        internal bool isClient = false;
        public int planetDetectionSkip = 0;
        public int dragSkip = 0;
        public float dragMultiplier = 1f;
        public ChatCommandHandler chatCommandHandler;

        public override void SaveData()
        {
            SaveSettings();
        }

        public override void LoadData()
        {
            // amogst the earliest execution points, but not everything is available at this point.
            Instance = this;
        }

        protected override void UnloadData()
        {
            Instance = null;

            if(isServer)
            {
                MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
                MyAPIGateway.Entities.OnEntityRemove -= EntityRemoved;

                planets.Clear();
                removePlanets.Clear();
                entityList.Clear();
                grids.Clear();

                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HANDLER_ID_SET, OnMessageFromClient);
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HANDLER_ID_GET, OnMessageFromClient);
            }

            if(isClient)
            {
                chatCommandHandler.Stop();

                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HANDLER_ID_RESPOND, OnMessageFromServer);
            }
        }

        public void ClientInit()
        {
            isClient = true;

            // Set up chat command event
            chatCommandHandler = new ChatCommandHandler();
            chatCommandHandler.Commands.Add("atmodrag", DragMultiplierCommand);

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HANDLER_ID_RESPOND, OnMessageFromServer);
        }

        public void ServerInit()
        {
            isServer = true;

            LoadSettings();

            entityList.Clear();

            // Populate Entity List
            MyAPIGateway.Entities.GetEntities(entityList);

            // Event handlers for initial adding/removing
            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
            MyAPIGateway.Entities.OnEntityRemove += EntityRemoved;

            foreach (var entity in entityList)
            {
                EntityAdded(entity);
            }

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HANDLER_ID_SET, OnMessageFromClient);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HANDLER_ID_GET, OnMessageFromClient);
        }

        public override void UpdateAfterSimulation()
        {
            // Checks
            if (!initalized)
            {
                initalized = true;
                if(!MyAPIGateway.Multiplayer.MultiplayerActive || MyAPIGateway.Multiplayer.IsServer)
                {
                    ServerInit();
                }

                if(!MyAPIGateway.Multiplayer.MultiplayerActive || !MyAPIGateway.Multiplayer.IsServer)
                {
                    ClientInit();
                }
            }

            if(!isServer) return;

            // Planet Detection
            if (--planetDetectionSkip < 0)
            {
                planetDetectionSkip = SKIP_TICKS_3600;
                MyAPIGateway.Entities.GetEntities(entityList);

                // Add planets to planet list
                foreach(var e in entityList)
                {
                    if (e is MyPlanet)
                    {
                        if (e.Closed || e.MarkedForClose)
                            removePlanets.Add(e.EntityId);
                        if (!planets.ContainsKey(e.EntityId))
                            planets.Add(e.EntityId, e as MyPlanet);
                    }
                }

                // Remove deleted planets from planet list
                if (removePlanets.Count > 0)
                {
                    foreach (var id in removePlanets)
                        planets.Remove(id);
                    removePlanets.Clear();
                }
            }

            // Attempt to apply drag to grids
            foreach (var entity in grids.Values)
            {
                Grid gridObj = entity;
                if (gridObj != null)
                    if (gridObj.grid != null && !gridObj.grid.Closed && gridObj.grid.Physics != null && !gridObj.grid.Physics.IsStatic && gridObj.grid.BlocksCount > 3)
                        gridObj.calculateAndApplyDrag();
            }
        }

        public void EntityAdded(IMyEntity entity)
        {
            if (entity as IMyCubeGrid != null)
            {
                if (entity == null)
                    return;
                if (entity is IMyCubeGrid)
                {
                    Grid gridObj;
                    if (grids.TryGetValue(entity.EntityId, out gridObj))
                        grids.Remove(entity.EntityId);
                    gridObj = new Grid(entity);
                    grids.Add(entity.EntityId, gridObj);
                }
            }
        }

        public void EntityRemoved(IMyEntity entity)
        {
            if (entity == null)
                return;
            if (entity is IMyCubeGrid)
            {
                Grid gridObj;
                if (grids.TryGetValue(entity.EntityId, out gridObj))
                    grids.Remove(entity.EntityId);
            }
        }

        private void DragMultiplierCommand(string[] args)
        {
            if(MyAPIGateway.Session.PromoteLevel < MyPromoteLevel.Admin)
            {
                MyAPIGateway.Utilities.ShowMessage("AtmosphericDrag", "You don't have permission to use this command.");
                return;
            }

            if(args.Length == 1)
            {
                if(isServer)
                {
                    MyAPIGateway.Utilities.ShowMessage("AtmosphericDrag", $"Current drag multiplier: {dragMultiplier}");
                }
                else
                {
                    MyAPIGateway.Multiplayer.SendMessageToServer(HANDLER_ID_GET, null);
                }

                return;
            }

            if(args.Length > 2)
            {
                MyAPIGateway.Utilities.ShowMessage("AtmosphericDrag", "Error: Unrecognized arguments.");
                return;
            }

            try
            {
                dragMultiplier = Math.Max(0f, float.Parse(args[1], CultureInfo.InvariantCulture));
                if(!isServer)
                {
                    MyAPIGateway.Multiplayer.SendMessageToServer(HANDLER_ID_SET, MyAPIGateway.Utilities.SerializeToBinary<float>(dragMultiplier));
                }

                MyAPIGateway.Utilities.ShowMessage("AtmosphericDrag", $"Set drag multiplier to {dragMultiplier}");
            }
            catch(FormatException)
            {
                MyAPIGateway.Utilities.ShowMessage("AtmosphericDrag", "Error: Invalid argument.");
                return;
            }
        }

        private void SaveSettings()
        {
            if(isServer)
            {
                TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(CONFIG_FILE, typeof(AtmosphericDrag));
                writer.WriteLine($"DRAG_MULTIPLIER: {dragMultiplier}");
                writer.Close();
            }
        }

        private void LoadSettings()
        {
            if(isServer)
            {
                if(MyAPIGateway.Utilities.FileExistsInWorldStorage(CONFIG_FILE, typeof(AtmosphericDrag)))
                {
                    TextReader file = MyAPIGateway.Utilities.ReadFileInWorldStorage(CONFIG_FILE, typeof(AtmosphericDrag));
                    string line = file.ReadLine();

                    while(line != null)
                    {
                        if(line.StartsWith("DRAG_MULTIPLIER"))
                        {
                            try
                            {
                                dragMultiplier = Math.Max(0f, float.Parse(line.Split(':')[1].Trim(), CultureInfo.InvariantCulture));
                            }
                            catch(FormatException) {}
                        }

                        line = file.ReadLine();
                    }

                    file.Close();
                }
            }
        }

        private void OnMessageFromClient(ushort id, byte[] data, ulong senderID, bool reliable)
        {
            switch(id)
            {
                case HANDLER_ID_SET:
                    try
                    {
                        dragMultiplier = MyAPIGateway.Utilities.SerializeFromBinary<float>(data);
                    }
                    catch
                    {
                        MyLog.Default.WriteLine($"[AtmosphericDragConfigurable] Error: Failed to set drag multiplier from data recieved from client {senderID}.");
                    }

                    break;
                
                case HANDLER_ID_GET:
                    MyAPIGateway.Multiplayer.SendMessageTo(HANDLER_ID_RESPOND, MyAPIGateway.Utilities.SerializeToBinary<float>(dragMultiplier), senderID);
                    break;
            }
        }

        private void OnMessageFromServer(ushort id, byte[] data, ulong senderID, bool reliable)
        {
            try
            {
                dragMultiplier = MyAPIGateway.Utilities.SerializeFromBinary<float>(data);
                MyAPIGateway.Utilities.ShowMessage("AtmosphericDrag", $"Current drag multiplier: {dragMultiplier}");
            }
            catch
            {
                MyLog.Default.WriteLine($"[AtmosphericDragConfigurable] Error: Failed to get drag multiplier from data recieved from server.");
            }
        }
    }
}