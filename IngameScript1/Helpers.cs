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
        IMyTextSurface debugLcdSurface;

        public void InitDebugLcd()
        {
            //try find a debug lcd
            try
            {
                IMyTextSurfaceProvider lcdBlock = (IMyTextSurfaceProvider)GridTerminalSystem.GetBlockWithName("LCD Panel");
                debugLcdSurface = lcdBlock.GetSurface(0);
                debugLcdSurface.ContentType = ContentType.TEXT_AND_IMAGE;
                Echo = EchoLcd;
            }
            catch (Exception)
            {
                if (debug)
                    DebugEcho("No debug \"LCD Panel\" found");
            };
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
    }
}
