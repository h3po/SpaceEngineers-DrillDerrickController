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

        //Configuration
        float drillSpeedMetersPerSecond = 1.5f;
        #endregion

        //Main Code
        IMyTextSurface lcdSurface;
        bool firstPrint = true;

        //store pistons in lists keyed by direction
        Dictionary<Base6Directions.Direction, List<IMyPistonBase>> armPistons = Base6Directions.EnumDirections.ToDictionary(d => d, d => new List<IMyPistonBase>());

        IMyMotorStator mainRotor;

        public Program()
        {
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

            sortPistonsByDirection(firstPiston, referenceMatrix, pistonsByGrid);

            //print pistons to screen
            foreach (Base6Directions.Direction dir in Base6Directions.EnumDirections)
            {
                EchoLcd(dir.ToString() + ":");
                foreach (IMyPistonBase piston in armPistons[dir])
                {
                    EchoLcd(piston.CustomName);
                }
            }
        }

        //walk the chain of connected piston grids and put them into ordered lists separated by direction
        void sortPistonsByDirection(IMyPistonBase startPiston, MatrixD referenceMatrix, Dictionary<IMyCubeGrid, IMyPistonBase> pistonsByGrid)
        {
            IMyCubeGrid nextGrid = startPiston.Top.CubeGrid;
            IMyPistonBase nextPiston;

            //the next grid has a piston
            if (pistonsByGrid.TryGetValue(nextGrid, out nextPiston))
            {
                //store the piston in our dict of lists keyed by direction
                armPistons[getPistonDirection(referenceMatrix, nextPiston)].Add(nextPiston);

                //recurse with the next piston as the new start piston
                sortPistonsByDirection(nextPiston, referenceMatrix, pistonsByGrid);
            }

            //reached the end of the piston chain
            return;
        }

        Base6Directions.Direction getPistonDirection(MatrixD referenceMatrix, IMyPistonBase piston)
        {
            return Base6Directions.GetClosestDirection(Vector3D.TransformNormal(piston.WorldMatrix.Up, MatrixD.Transpose(referenceMatrix)));
        }

        public void EchoLcd(string text)
        {
            Echo(text);
            lcdSurface.WriteText(text + "\n", !firstPrint);
            firstPrint = false;
        }

        public void Save() { }

        public void Main(string argument, UpdateType updateSource) { }
    }
}
