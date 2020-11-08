using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;
using VRage.Game.ModAPI;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk preserve
        #region mdk macros
        //This script was deployed at $MDK_DATETIME$
        #endregion

        const string configName = "DrillDerrickController";
        #endregion

        //config defaults
        MyIni config = new MyIni();
        MyIni state = new MyIni();
        float drillSpeed = 1.5f;
        float moveSpeed = 10.0f;
        float drillRadius = 2.5f;
        bool debug = true;

        //state
        float inwardProgress = 0; float downwardProgress = 0;
        bool firstPrint = true;
        bool initialized = false;
        bool running = false;

        public Program()
        {
            try
            {
                InitDebugLcd();

                //Load Config from CustomData
                DebugEcho("Loading config from CustomData");
                MyIniParseResult result;
                if (!config.TryParse(Me.CustomData, out result))
                    throw new Exception("CustomData could not be parsed:\n" + result.ToString());

                drillSpeed = config.Get(configName, "drillSpeed").ToSingle(drillSpeed);
                moveSpeed = config.Get(configName, "moveSpeed").ToSingle(moveSpeed);
                drillRadius = config.Get(configName, "drillDepth").ToSingle(drillRadius);
                DebugEcho($"Drill Speed: {drillSpeed}, Move Speed: {moveSpeed}, Drill Depth: {drillRadius}");

                if (!config.ContainsSection(configName))
                {
                    Save();
                    Me.CustomData = Storage;
                    DebugEcho("Initialized CustomData with default values");
                }

                //Load State from Storage
                DebugEcho("Loading state from storage");
                if (!state.TryParse(Me.CustomData, out result))
                    Echo("Storage could not be parsed, reinitializing");
                inwardProgress = state.Get(configName, "forwardProgress").ToSingle(0f);
                downwardProgress = state.Get(configName, "downwardProgress").ToSingle(0f);
                Echo($"Progress Inward: {inwardProgress}, Progress Down: {downwardProgress}");

                InitArm();

                initialized = true;
            }
            catch (Exception e)
            {
                // Dump the exception content
                Echo("An error occurred in Program().");
                Echo($"Exception: {e}\n---");

                // Rethrow the exception to make the programmable block halt execution properly
                throw;
            }

        }

        public void Save() {
            state.Set(configName, "forwardProgress", inwardProgress);
            state.Set(configName, "downwardProgress", downwardProgress);

            Storage = state.ToString();
        }

        public void Main(string argument, UpdateType updateSource) {

            const UpdateType tickUpdateTypes = (UpdateType.Once | UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100);
            const UpdateType externalUpdateTypes = (UpdateType.IGC | UpdateType.Mod | UpdateType.Script | UpdateType.Terminal | UpdateType.Trigger);

            try
            {
                argument = argument.ToLower();

                if (!initialized)
                {
                    Echo("Error: Not initialized");
                    return;
                }

                if ((updateSource & tickUpdateTypes) != 0)
                {

                }

                if ((updateSource & externalUpdateTypes) != 0)
                {
                    if (!running)
                    {
                        running = true;
                        DebugEcho("Started running");
                    }
                    else
                    {
                        running = false;
                        DebugEcho("Stopped running");
                    }
                }
            }
            catch (Exception e)
            {
                // Dump the exception content
                Echo("An error occurred in Main().");
                Echo($"Exception: {e}\n---");

                // Rethrow the exception to make the programmable block halt execution properly
                throw;
            }
        }
    }
}
