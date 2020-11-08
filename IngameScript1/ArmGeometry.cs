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

namespace IngameScript
{
    partial class Program
    {
        //blocks
        //store pistons in lists keyed by direction
        Dictionary<Base6Directions.Direction, List<PistonMotionController>> armPistonControllers = Base6Directions.EnumDirections.ToDictionary(d => d, d => new List<PistonMotionController>());
        RotorMotionController rotorController;
        List<IMyShipDrill> drills = new List<IMyShipDrill>();

        //arm shape
        float minArmFwd = 0; float curArmFwd = 0; float maxArmFwd = 0; float targetArmFwd = 0;
        float minArmDown = 0; float curArmDown = 0; float maxArmDown = 0; float targetArmDown = 0;
        float minArmRadius = 0; //this is the outermost edge of the hole if the arm is fully retracted, not the innermost
        float maxArmRadius = 0;

        void InitArm()
        {
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

            armPistonControllers[Base6Directions.Direction.Forward].Add(new PistonMotionController(firstPiston, this));

            //get the rotor on the main grid
            //this is considered facing "down"
            rotorController = new RotorMotionController((IMyMotorStator)GridTerminalSystem.GetBlockWithName("Rotor 1"), this);

            //make the reference direction matrix
            MatrixD referenceMatrix = new MatrixD();
            referenceMatrix.Forward = firstPiston.WorldMatrix.Up;
            referenceMatrix.Up = rotorController.rotor.WorldMatrix.Down;
            referenceMatrix.Left = firstPiston.WorldMatrix.Up.Cross(rotorController.rotor.WorldMatrix.Down);
            MatrixD referenceMatrixTransposed = MatrixD.Transpose(referenceMatrix);

            //get the drills and pistons
            IMyCubeGrid drillGrid = SortPistonsByDirection(firstPiston, referenceMatrixTransposed, pistonsByGrid);
            GridTerminalSystem.GetBlocksOfType(drills);
            if (drills.Count() == 0)
            {
                Echo("Error: No drills");
                return;
            }
            DebugEcho($"Found {drills.Count()} drills");
            DebugEcho($"Found {armPistonControllers[Base6Directions.Direction.Forward].Count() + armPistonControllers[Base6Directions.Direction.Backward].Count()} horizontal pistons");
            DebugEcho($"Found {armPistonControllers[Base6Directions.Direction.Up].Count() + armPistonControllers[Base6Directions.Direction.Down].Count()} vertical pistons");

            foreach (PistonMotionController pistonController in armPistonControllers[Base6Directions.Direction.Forward])
            {
                minArmFwd += pistonController.piston.MinLimit;
                curArmFwd += pistonController.piston.CurrentPosition;
                maxArmFwd += pistonController.piston.MaxLimit;
            }

            foreach (PistonMotionController pistonController in armPistonControllers[Base6Directions.Direction.Backward])
            {
                minArmFwd -= pistonController.piston.MaxLimit;
                curArmFwd -= pistonController.piston.CurrentPosition;
                maxArmFwd -= pistonController.piston.MinLimit;
            }

            curArmFwd -= minArmFwd;
            maxArmFwd -= minArmFwd;
            minArmFwd = 0;

            foreach (PistonMotionController pistonController in armPistonControllers[Base6Directions.Direction.Down])
            {
                minArmDown += pistonController.piston.MinLimit;
                curArmDown += pistonController.piston.CurrentPosition;
                maxArmDown += pistonController.piston.MaxLimit;
            }

            foreach (PistonMotionController pistonController in armPistonControllers[Base6Directions.Direction.Up])
            {
                minArmDown -= pistonController.piston.MaxLimit;
                curArmDown -= pistonController.piston.CurrentPosition;
                maxArmDown -= pistonController.piston.MinLimit;
            }

            curArmDown -= minArmDown;
            maxArmDown -= minArmDown;
            minArmDown = 0;

            DebugEcho($"Reach Forward: Min {minArmFwd}, Cur {curArmFwd}, Max {maxArmFwd}");
            DebugEcho($"Reach Down: Min {minArmDown}, Cur {curArmDown}, Max {maxArmDown}");

            //figure out which drill is furthest from the rotation axis
            Vector3D rotorpos = rotorController.rotor.GetPosition();
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
        }

        //walk the chain of connected piston grids and put them into ordered lists separated by direction
        IMyCubeGrid SortPistonsByDirection(IMyPistonBase startPiston, MatrixD referenceMatrixTransposed, Dictionary<IMyCubeGrid, IMyPistonBase> pistonsByGrid)
        {
            IMyCubeGrid nextGrid = startPiston.Top.CubeGrid;
            IMyPistonBase nextPiston;

            //the next grid has a piston
            if (pistonsByGrid.TryGetValue(nextGrid, out nextPiston))
            {
                //store the piston in our dict of lists keyed by direction
                armPistonControllers[GetPistonDirection(referenceMatrixTransposed, nextPiston)].Add(new PistonMotionController(nextPiston, this));

                //recurse with the next piston as the new start piston
                SortPistonsByDirection(nextPiston, referenceMatrixTransposed, pistonsByGrid);
            }

            //reached the end of the piston chain, return the last grid
            return nextGrid;
        }

        Base6Directions.Direction GetPistonDirection(MatrixD referenceMatrixTransposed, IMyPistonBase piston)
        {
            return Base6Directions.GetClosestDirection(Vector3D.TransformNormal(piston.WorldMatrix.Up, referenceMatrixTransposed));
        }
    }
}
