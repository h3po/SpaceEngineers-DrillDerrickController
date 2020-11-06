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
using IMyCubeGrid = VRage.Game.ModAPI.Ingame.IMyCubeGrid;

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
        float drillRadius = 2.5f;
        bool debug = true;

        //state
        float forwardProgress = 0; float downwardProgress = 0;
        bool firstPrint = true;
        bool initialized = false;

        //arm shape
        float minArmFwd = 0; float curArmFwd = 0; float maxArmFwd = 0;
        float minArmDown = 0; float curArmDown = 0; float maxArmDown = 0;
        float minArmRadius = 0; //this is the outermost edge of the hole if the arm is fully retracted, not the innermost
        float maxArmRadius = 0;

        //blocks
        IMyTextSurface debugLcdSurface;
        //store pistons in lists keyed by direction
        Dictionary<Base6Directions.Direction, List<IMyPistonBase>> armPistons = Base6Directions.EnumDirections.ToDictionary(d => d, d => new List<IMyPistonBase>());
        IMyMotorStator mainRotor;
        List<IMyShipDrill> drills = new List<IMyShipDrill>();

        public Program()
        {
            try
            {
                try
                {
                    IMyTextSurfaceProvider lcdBlock = (IMyTextSurfaceProvider)GridTerminalSystem.GetBlockWithName("LCD Panel");
                    debugLcdSurface = lcdBlock.GetSurface(0);
                    debugLcdSurface.ContentType = ContentType.TEXT_AND_IMAGE;
                    Echo = EchoLcd;
                }
                catch (Exception) {
                    if (debug)
                        DebugEcho("No debug \"LCD Panel\" found");
                };

                //Load Config from CustomData
                DebugEcho("Loading config from CustomData");
                MyIniParseResult result;
                if (!config.TryParse(Me.CustomData, out result))
                    throw new Exception("CustomData could not be parsed:\n" + result.ToString());

                drillSpeed = config.Get(configName, "drillSpeed").ToSingle(drillSpeed);
                drillRadius = config.Get(configName, "drillDepth").ToSingle(drillRadius);
                DebugEcho($"Drill Speed: {drillSpeed}, Drill Depth: {drillRadius}");

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
                forwardProgress = state.Get(configName, "forwardProgress").ToSingle(0f);
                downwardProgress = state.Get(configName, "downwardProgress").ToSingle(0f);
                Echo($"Progress Forward: {forwardProgress}, Progress Down: {downwardProgress}");

                //store pistons in dict keyed by cubegrid
                List<IMyPistonBase> allPistons = new List<IMyPistonBase>();
                GridTerminalSystem.GetBlocksOfType(allPistons);
                Dictionary<IMyCubeGrid, IMyPistonBase> pistonsByGrid = null;
                try
                {
                    pistonsByGrid = allPistons.ToDictionary(p => p.CubeGrid, p => p);
                }
                catch (System.ArgumentException)
                {
                    Echo("Error: Found multiple pistons on the same Grid - Your Subgrids may have merged together");
                    return;
                }

                //get the first piston on the main grid
                //this is considered facing "forward"
                IMyPistonBase firstPiston = null;
                try
                {
                    firstPiston = pistonsByGrid[Me.CubeGrid];
                }
                catch (System.Collections.Generic.KeyNotFoundException)
                {
                    Echo("Error: No piston found on the PB's grid");
                    return;
                }
                
                armPistons[Base6Directions.Direction.Forward].Add(firstPiston);

                //get the rotor on the main grid
                //this is considered facing "down"
                mainRotor = (IMyMotorStator)GridTerminalSystem.GetBlockWithName("Rotor 1");

                //make the reference direction matrix
                MatrixD referenceMatrix = new MatrixD();
                referenceMatrix.Forward = firstPiston.WorldMatrix.Up;
                referenceMatrix.Up = mainRotor.WorldMatrix.Down;
                referenceMatrix.Left = firstPiston.WorldMatrix.Up.Cross(mainRotor.WorldMatrix.Down);
                MatrixD referenceMatrixTransposed = MatrixD.Transpose(referenceMatrix);

                //get the drills and pistons
                IMyCubeGrid drillGrid = sortPistonsByDirection(firstPiston, referenceMatrixTransposed, pistonsByGrid);
                GridTerminalSystem.GetBlocksOfType(drills);
                if (drills.Count() == 0)
                {
                    Echo("Error: No drills");
                    return;
                }
                DebugEcho($"Found {drills.Count()} drills");

                ////print config
                //Echo(config.ToString());

                ////print pistons to screen
                //foreach (Base6Directions.Direction dir in Base6Directions.EnumDirections)
                //{
                //    EchoLcd(dir.ToString() + ":");
                //    foreach (IMyPistonBase piston in armPistons[dir])
                //    {
                //        EchoLcd(piston.CustomName);
                //    }
                //}

                DebugEcho($"Found {armPistons[Base6Directions.Direction.Forward].Count() + armPistons[Base6Directions.Direction.Backward].Count()} horizontal pistons");
                DebugEcho($"Found {armPistons[Base6Directions.Direction.Up].Count() + armPistons[Base6Directions.Direction.Down].Count()} vertical pistons");

                foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Forward])
                {
                    minArmFwd += piston.MinLimit;
                    curArmFwd += piston.CurrentPosition;
                    maxArmFwd += piston.MaxLimit;
                }

                foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Backward])
                {
                    minArmFwd -= piston.MaxLimit;
                    curArmFwd -= piston.CurrentPosition;
                    maxArmFwd -= piston.MinLimit;
                }

                curArmFwd -= minArmFwd;
                maxArmFwd -= minArmFwd;
                minArmFwd = 0;

                foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Down])
                {
                    minArmDown += piston.MinLimit;
                    curArmDown += piston.CurrentPosition;
                    maxArmDown += piston.MaxLimit;
                }

                foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Up])
                {
                    minArmDown -= piston.MaxLimit;
                    curArmDown -= piston.CurrentPosition;
                    maxArmDown -= piston.MinLimit;
                }

                curArmDown -= minArmDown;
                maxArmDown -= minArmDown;
                minArmDown = 0;

                DebugEcho($"Reach Forward: Min {minArmFwd}, Cur {curArmFwd}, Max {maxArmFwd}");
                DebugEcho($"Reach Down: Min {minArmDown}, Cur {curArmDown}, Max {maxArmDown}");

                //figure out which drill is furthest from the rotation axis
                Vector3D rotorpos = mainRotor.GetPosition();
                foreach (IMyShipDrill drill in drills)
                {
                    Vector3D worldDist = drill.GetPosition() - rotorpos;
                    Vector3D armDist = Vector3D.TransformNormal(worldDist, referenceMatrixTransposed);
                    armDist.Z *= -1;
                    armDist.X *= -1;
                    //EchoLcd($"{drill.CustomName}\nfwd:{armDist.Z}\nup:{armDist.Y}\nleft:{armDist.X}");

                    //ignore the distance in the vertical direction
                    armDist.Y = 0;

                    //adjust for the current extension of the arm
                    armDist.Z -= curArmFwd;

                    float horizontalDist = (float)armDist.Length();
                    //EchoLcd($"{drill.CustomName}: {horizontalDist}");
                    if (horizontalDist > minArmRadius)
                        minArmRadius = horizontalDist;
                }

                maxArmRadius = minArmRadius + maxArmFwd;
                DebugEcho($"Minimum Radius: {minArmRadius}\nMaxium Radius: {maxArmRadius}");

                initialized = true;
            }
            catch (Exception e)
            {
                // Dump the exception content
                Echo("An error occurred during script execution.");
                Echo($"Exception: {e}\n---");

                // Rethrow the exception to make the programmable block halt execution properly
                throw;
            }

        }

        //walk the chain of connected piston grids and put them into ordered lists separated by direction
        IMyCubeGrid sortPistonsByDirection(IMyPistonBase startPiston, MatrixD referenceMatrixTransposed, Dictionary<IMyCubeGrid, IMyPistonBase> pistonsByGrid)
        {
            IMyCubeGrid nextGrid = startPiston.Top.CubeGrid;
            IMyPistonBase nextPiston;

            //the next grid has a piston
            if (pistonsByGrid.TryGetValue(nextGrid, out nextPiston))
            {
                //store the piston in our dict of lists keyed by direction
                armPistons[getPistonDirection(referenceMatrixTransposed, nextPiston)].Add(nextPiston);

                //recurse with the next piston as the new start piston
                sortPistonsByDirection(nextPiston, referenceMatrixTransposed, pistonsByGrid);
            }

            //reached the end of the piston chain, return the last grid
            return nextGrid;
        }

        Base6Directions.Direction getPistonDirection(MatrixD referenceMatrixTransposed, IMyPistonBase piston)
        {
            return Base6Directions.GetClosestDirection(Vector3D.TransformNormal(piston.WorldMatrix.Up, referenceMatrixTransposed));
        }

        public void EchoLcd(string text)
        {
            debugLcdSurface.WriteText(text + "\n", !firstPrint);
            firstPrint = false;
        }

        public void DebugEcho(string text)
        {
            if (debug) Echo(text);
        }

        public void Save() {
            state.Set(configName, "forwardProgress", forwardProgress);
            state.Set(configName, "downwardProgress", downwardProgress);

            Storage = state.ToString();
        }

        public void Main(string argument, UpdateType updateSource) { }
    }
}
