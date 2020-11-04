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
using static VRageMath.Base6Directions;
using VRage.Game.ModAPI;
using IMyCubeGrid = VRage.Game.ModAPI.Ingame.IMyCubeGrid;
using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        IMyTextSurface lcdSurface;
        bool firstPrint = true;

        List<IMyPistonBase> outwardPistons = new List<IMyPistonBase>();
        List<IMyPistonBase> inwardPistons = new List<IMyPistonBase>();
        List<IMyPistonBase> upwardPistons = new List<IMyPistonBase>();
        List<IMyPistonBase> downwardPistons = new List<IMyPistonBase>();
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
            //this is considered facing "outward"
            IMyPistonBase firstPiston = pistonsByGrid[Me.CubeGrid];
            outwardPistons.Add(firstPiston);

            //get the rotor on the main grid
            //this is considered facing "down"
            mainRotor = (IMyMotorStator)GridTerminalSystem.GetBlockWithName("Rotor 1");

            //make the reference direction matrix
            MatrixD referenceMatrix = new MatrixD();
            referenceMatrix.Forward = firstPiston.WorldMatrix.Up;
            referenceMatrix.Up = mainRotor.WorldMatrix.Down;
            referenceMatrix.Left = firstPiston.WorldMatrix.Up.Cross(mainRotor.WorldMatrix.Down);

            sortPistonsByDirection(firstPiston, referenceMatrix, pistonsByGrid);

            EchoLcd("Outward: ");
            foreach (IMyPistonBase piston in outwardPistons)
            {
                EchoLcd(piston.CustomName);
            }
            EchoLcd("Inward: ");
            foreach (IMyPistonBase piston in inwardPistons)
            {
                EchoLcd(piston.CustomName);
            }
            EchoLcd("Up: ");
            foreach (IMyPistonBase piston in upwardPistons)
            {
                EchoLcd(piston.CustomName);
            }
            EchoLcd("Down: ");
            foreach (IMyPistonBase piston in downwardPistons)
            {
                EchoLcd(piston.CustomName);
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
                Base6Directions.Direction nextDirection = getPistonDirection(referenceMatrix, nextPiston);

                //the next piston faces the same way as the reference
                if (nextDirection == Base6Directions.Direction.Forward)
                {
                    outwardPistons.Add(nextPiston);
                }
                //same axis, but inverted
                else if (nextDirection == Base6Directions.Direction.Backward)
                {
                    inwardPistons.Add(nextPiston);
                }
                else if (nextDirection == Base6Directions.Direction.Up)
                {
                    upwardPistons.Add(nextPiston);
                }
                else if (nextDirection == Base6Directions.Direction.Down)
                {
                    downwardPistons.Add(nextPiston);
                }
                else
                {
                    EchoLcd("sideways piston:" + nextPiston.CustomName);
                }

                //recurse with the next piston as the new start piston
                sortPistonsByDirection(nextPiston, referenceMatrix, pistonsByGrid);
            }
            //no more pistons
            else
            {
                return;
            }

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
