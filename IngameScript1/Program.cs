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

        //config defaults - you may change these in the PB's CustomData
        float drillSpeed = 1.5f;        //[m/s] the speed we assume our drill can move without hitting the sides of the hole
        float drillHoleDiameter = 2.5f; //[m]   the diameter of the hole our dill makes
        float moveSpeed = 10.0f;        //[m/s] the speed our arm is allowed to move when not drilling

        //you may switch between configs in the CustomData by changing this
        const string configName = "DrillDerrickController";
        #endregion

        bool debug = true;
        MyIni config = new MyIni();
        MyIni state = new MyIni();

        //state
        float inwardProgress = 0; float downwardProgress = 0;
        bool initialized = false;
        bool running = false;
        IEnumerator<bool> stateMachine;
        bool stateMachineHasMoreSteps = true;

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
                drillHoleDiameter = config.Get(configName, "drillDepth").ToSingle(drillHoleDiameter);
                DebugEcho($"Drill Speed: {drillSpeed}, Move Speed: {moveSpeed}, Drill Depth: {drillHoleDiameter}");

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
                inwardProgress = 0; //state.Get(configName, "forwardProgress").ToSingle(0f);
                downwardProgress = 0; //state.Get(configName, "downwardProgress").ToSingle(0f);
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

            UpdateFrequency requiredFrequency = UpdateFrequency.None;

            try
            {
                argument = argument.ToLower();

                if (!initialized)
                {
                    Echo("Error: Not initialized");
                    return;
                }

                if (running)
                {
                    if ((updateSource & tickUpdateTypes) != 0)
                    {
                        

                        foreach (List<PistonMotionController> pistonControllers in armPistonControllers.Values)
                        {
                            foreach (PistonMotionController pistonController in pistonControllers)
                            {
                                requiredFrequency |= pistonController.Update(updateSource);
                            }
                        }

                        requiredFrequency |= rotorController.Update(updateSource);
                    }

                    if (stateMachineHasMoreSteps && ((updateSource & UpdateType.Update100) != 0))
                    {
                        stateMachineHasMoreSteps = stateMachine.MoveNext();
                        if (stateMachineHasMoreSteps) requiredFrequency |= UpdateFrequency.Once;
                    }

                    requiredFrequency |= UpdateFrequency.Update100;
                }

                if ((updateSource & externalUpdateTypes) != 0)
                {
                    if (!running)
                    {
                        running = true;
                        DebugEcho("Started running");
                        requiredFrequency |= UpdateFrequency.Update100;
                        stateMachine = DrillHole();
                    }
                    else
                    {
                        running = false;
                        DebugEcho("Stopped running");
                        requiredFrequency = UpdateFrequency.None;
                        stateMachine.Dispose();
                    }
                }

                Runtime.UpdateFrequency = requiredFrequency;

                //show update frequency with screen color
                if ((requiredFrequency & UpdateFrequency.Update1) != 0)
                {
                    Me.GetSurface(0).BackgroundColor = Color.Red;
                }
                else if ((requiredFrequency & UpdateFrequency.Update10) != 0)
                {
                    Me.GetSurface(0).BackgroundColor = Color.Orange;
                }
                else if ((requiredFrequency & UpdateFrequency.Update100) != 0)
                {
                    Me.GetSurface(0).BackgroundColor = Color.Green;
                }
                else
                {
                    Me.GetSurface(0).BackgroundColor = Color.Black;
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

        //state machine that controls the overall hole drilling progress
        IEnumerator<bool> DrillHole()
        {
            bool continueFromSavedProgress = true;
            IEnumerator<bool> extendArmFwd;

            //step downward by drillHoleDiameter from top to bottom
            for (float targetArmDown = (minArmDown + downwardProgress); targetArmDown <= maxArmDown; targetArmDown += drillHoleDiameter)
            {
                DebugEcho($"Starting horizontal plane at depth {targetArmDown}");
                //move to maxArmFwd at move speed
                extendArmFwd = ExtendArmFwd(maxArmFwd, moveSpeed);
                while (extendArmFwd.MoveNext()) yield return true;
                extendArmFwd.Dispose();
                //TODO move to targetArmDown at drill speed

                float startArmFwd = maxArmFwd;

                if (continueFromSavedProgress)
                {
                    startArmFwd -= inwardProgress;
                    continueFromSavedProgress = false;
                }

                //step inward by drillHoleDiameter from outer to inner radius
                for (float targetArmFwd = startArmFwd; targetArmFwd >= 0; targetArmFwd -= drillHoleDiameter)
                {
                    DebugEcho($"Starting rotation at {targetArmFwd}");
                    //move to targetArmFwd at drill speed
                    extendArmFwd = ExtendArmFwd(targetArmFwd, drillSpeed);
                    while (extendArmFwd.MoveNext()) yield return true;
                    extendArmFwd.Dispose();

                    DebugEcho("Waiting for rotor");
                    //complete one rotation at drill speed
                    rotorController.setSpeed = MathHelper.RPMToRadiansPerSecond * 10f;
                    rotorController.SetTarget(0);
                    while (!MathHelper.IsZero(rotorController.rotor.TargetVelocityRad)) yield return true;

                    inwardProgress = maxArmFwd - targetArmFwd;
                    DebugEcho("Rotation complete");
                }

                downwardProgress = targetArmDown;
            }

            DebugEcho("State machine done");

            //park position
            //TODO move to minArmDown at move speed
            //TODO move to minArmFwd at move speed

            yield return false;
        }

        IEnumerator<bool> WaitForPistonControllers(List<PistonMotionController> controllers)
        {
            foreach (PistonMotionController pistonController in controllers)
            {
                if (!MathHelper.IsZero(pistonController.piston.Velocity))
                {
                    DebugEcho($"{pistonController.piston.CustomName} speed not 0");
                    yield return true;
                }
            }

            DebugEcho("Waiting done");
            yield return false;
        }

        //state machine that controls the horizontal pistons
        IEnumerator<bool> ExtendArmFwd(float position, float maxSpeed)
        {
            float distanceRemaining = position;
            float speedRemaining = maxSpeed;
            IEnumerator<bool> waitForPistonControllers;

            DebugEcho($"Extending pistons to {position} at {maxSpeed}");

            //retract inward pistons in order from beginning of arm to end of arm
            for (int i = 0; i < armPistonControllers[Base6Directions.Direction.Backward].Count(); i++)
            {
                //if (MathHelper.IsZero(distanceRemaining)) break;

                PistonMotionController pistonController = armPistonControllers[Base6Directions.Direction.Backward][i];
                IMyPistonBase piston = pistonController.piston;
                float availableDistance = piston.MaxLimit - piston.MinLimit;
                float availableSpeed = piston.MaxVelocity;
                float usedDistance = Math.Min(availableDistance, distanceRemaining);
                float usedSpeed = Math.Min(availableSpeed, speedRemaining);
                float target = piston.MaxLimit - usedDistance;

                //pistonController.EnableDebug();
                DebugEcho($"{piston.CustomName}: target {target:0.#}, speed {usedSpeed:0.#}");

                pistonController.SetTarget(target);
                pistonController.setSpeed = usedSpeed;
                distanceRemaining -= usedDistance;
                speedRemaining -= usedSpeed;

                //DebugEcho($"Distance remaining: {distanceRemaining}");
                //DebugEcho($"Speed remaining: {speedRemaining}");
                if (speedRemaining <= 0)
                {
                    waitForPistonControllers = WaitForPistonControllers(armPistonControllers[Base6Directions.Direction.Backward]);
                    DebugEcho("Waiting for inverse pistons");
                    while (waitForPistonControllers.MoveNext())
                        yield return true;
                    waitForPistonControllers.Dispose();
                    speedRemaining = maxSpeed;
                }
            }

            waitForPistonControllers = WaitForPistonControllers(armPistonControllers[Base6Directions.Direction.Backward]);
            DebugEcho("Waiting for inverse pistons");
            while (waitForPistonControllers.MoveNext())
                yield return true;
            waitForPistonControllers.Dispose();

            //extend outward pistons in order from end of arm to beginning of arm
            for (int i = armPistonControllers[Base6Directions.Direction.Forward].Count() - 1; i >= 0; i--)
            {
                //if (MathHelper.IsZero(distanceRemaining)) break;

                PistonMotionController pistonController = armPistonControllers[Base6Directions.Direction.Forward][i];
                IMyPistonBase piston = pistonController.piston;
                float availableDistance = piston.MaxLimit - piston.MinLimit;
                float availableSpeed = piston.MaxVelocity;
                float usedDistance = Math.Min(availableDistance, distanceRemaining);
                float usedSpeed = Math.Min(availableSpeed, speedRemaining);
                float target = piston.MinLimit + usedDistance;

                //pistonController.EnableDebug();
                DebugEcho($"{piston.CustomName}: target {target:0.#}, speed {usedSpeed:0.#}");

                pistonController.SetTarget(target);
                pistonController.setSpeed = usedSpeed;
                distanceRemaining -= usedDistance;
                speedRemaining -= usedSpeed;

                //DebugEcho($"Distance remaining: {distanceRemaining}");
                //DebugEcho($"Speed remaining: {speedRemaining}");
                if (speedRemaining <= 0)
                {
                    waitForPistonControllers = WaitForPistonControllers(armPistonControllers[Base6Directions.Direction.Forward]);
                    DebugEcho("Waiting for outwards pistons");
                    while (waitForPistonControllers.MoveNext())
                        yield return true;
                    waitForPistonControllers.Dispose();
                    speedRemaining = maxSpeed;
                }
            }

            waitForPistonControllers = WaitForPistonControllers(armPistonControllers[Base6Directions.Direction.Forward]);
            DebugEcho("Waiting for outwards pistons");
            while (waitForPistonControllers.MoveNext())
                yield return true;
            waitForPistonControllers.Dispose();

            DebugEcho($"Extending arm to {position} done");
            yield return false;
        }
    }
}
