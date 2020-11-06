﻿using Sandbox.Game.EntityComponents;
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

        const string iniSection = "DrillDerrickController";
        #endregion

        //Main Code
        IMyTextSurface lcdSurface;
        bool firstPrint = true;

        //store pistons in lists keyed by direction
        Dictionary<Base6Directions.Direction, List<IMyPistonBase>> armPistons = Base6Directions.EnumDirections.ToDictionary(d => d, d => new List<IMyPistonBase>());

        IMyMotorStator mainRotor;
        List<IMyShipDrill> drills = new List<IMyShipDrill>();

        //config
        MyIni _ini = new MyIni();
        float drillSpeed, drillDepth, forwardProgress, downwardProgress;

        public Program()
        {
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
                throw new Exception("CustomData could not be parsed:\n" + result.ToString());

            Echo("Loading config");
            drillSpeed = _ini.Get(iniSection, "drillSpeed").ToSingle(1.5f);
            drillDepth = _ini.Get(iniSection, "drillDepth").ToSingle(1.5f);
            forwardProgress = _ini.Get(iniSection, "forwardProgress").ToSingle(0f);
            downwardProgress = _ini.Get(iniSection, "downwardProgress").ToSingle(0f);

            if (!_ini.ContainsSection(iniSection))
            {
                Echo("Initialized CustomData with default values");
                Save();
                return;
            }
                
            IMyTextSurfaceProvider lcdBlock = (IMyTextSurfaceProvider) GridTerminalSystem.GetBlockWithName("LCD Panel");
            lcdSurface = lcdBlock.GetSurface(0);
            lcdSurface.ContentType = ContentType.TEXT_AND_IMAGE;

            //store pistons in dict keyed by cubegrid
            List<IMyPistonBase> allPistons = new List<IMyPistonBase>();
            GridTerminalSystem.GetBlocksOfType(allPistons);
            Dictionary<IMyCubeGrid, IMyPistonBase> pistonsByGrid = allPistons.ToDictionary(p => p.CubeGrid, p => p);

            //get the first piston on the main grid
            //this is considered facing "forward"
            IMyPistonBase firstPiston = pistonsByGrid[Me.CubeGrid];
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
            EchoLcd($"Found {drills.Count()} drills");

            ////print config
            //Echo(_ini.ToString());

            ////print pistons to screen
            //foreach (Base6Directions.Direction dir in Base6Directions.EnumDirections)
            //{
            //    EchoLcd(dir.ToString() + ":");
            //    foreach (IMyPistonBase piston in armPistons[dir])
            //    {
            //        EchoLcd(piston.CustomName);
            //    }
            //}

            EchoLcd($"Found {armPistons[Base6Directions.Direction.Forward].Count() + armPistons[Base6Directions.Direction.Backward].Count()} horizontal pistons");
            EchoLcd($"Found {armPistons[Base6Directions.Direction.Up].Count() + armPistons[Base6Directions.Direction.Down].Count()} vertical pistons");

            float minFwd = 0; float curFwd = 0; float maxFwd = 0;

            foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Forward])
            {
                minFwd += piston.MinLimit;
                curFwd += piston.CurrentPosition;
                maxFwd += piston.MaxLimit;
            }

            foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Backward])
            {
                minFwd -= piston.MaxLimit;
                curFwd -= piston.CurrentPosition;
                maxFwd -= piston.MinLimit;
            }

            curFwd -= minFwd;
            maxFwd -= minFwd;
            minFwd = 0;

            float minDown = 0; float curDown = 0; float maxDown = 0;

            foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Down])
            {
                minDown += piston.MinLimit;
                curDown += piston.CurrentPosition;
                maxDown += piston.MaxLimit;
            }

            foreach (IMyPistonBase piston in armPistons[Base6Directions.Direction.Up])
            {
                minDown -= piston.MaxLimit;
                curDown -= piston.CurrentPosition;
                maxDown -= piston.MinLimit;
            }

            curDown -= minDown;
            maxDown -= minDown;
            minDown = 0;

            EchoLcd($"Reach Forward: Min {minFwd}, Cur {curFwd}, Max {maxFwd}");
            EchoLcd($"Reach Down: Min {minDown}, Cur {curDown}, Max {maxDown}");

            //this is the outermost edge of the hole if the arm is fully retracted, not the innermost
            float minRadius = 0; float maxRadius = 0;

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
                armDist.Z -= curFwd;

                float horizontalDist = (float)armDist.Length();
                //EchoLcd($"{drill.CustomName}: {horizontalDist}");
                if (horizontalDist > minRadius)
                    minRadius = horizontalDist;
            }

            maxRadius = minRadius + maxFwd;

            EchoLcd($"Minimum Radius: {minRadius}\nMaxium Radius: {maxRadius}");
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
            //Echo(text);
            lcdSurface.WriteText(text + "\n", !firstPrint);
            firstPrint = false;
        }

        public void Save() {
            Echo("Saving config");

            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
                throw new Exception(result.ToString());

            _ini.Set(iniSection, "drillSpeed", drillSpeed);
            _ini.Set(iniSection, "drillDepth", drillSpeed);
            _ini.Set(iniSection, "forwardProgress", forwardProgress);
            _ini.Set(iniSection, "downwardProgress", downwardProgress);

            Me.CustomData = _ini.ToString();
        }

        public void Main(string argument, UpdateType updateSource) { }
    }
}
