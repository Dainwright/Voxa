﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK;
using Voxa.Rendering;
using Voxa.Logic;
using Voxa.Utils;

namespace Voxa
{
    public class Engine
    {
        public const int              VERSION_MAJOR = 0;
        public const int              VERSION_MINOR = 1;

        public const int              WINDOW_WIDTH = 640;
        public const int              WINDOW_HEIGHT = 400;
        public const int              RENDER_DISTANCE = 1;
        public const double           TARGET_UPDATE_RATE = 60.0;

        private static EngineWindow   engineWindow;
        private static RenderingPool  renderingPool;
        private static UniformManager uniformManager;
        private static Game           game;

        public static Texture         WhitePixelTexture;

        public static EngineWindow    EngineWindow { get { return engineWindow; } }

        public static RenderingPool   RenderingPool { get { return renderingPool; } }

        public static UniformManager  UniformManager { get { return uniformManager; } }

        public static Game            Game { get { return game; } }

        public Engine()
        {
        }

        [STAThread]
        public static void Start(Game gameInstance)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Logger.AddStickyInfo("voxaInfo", new LoggerMessage("V O X A - Lite 3D Engine v" + VERSION_MAJOR + "." + VERSION_MINOR, ConsoleColor.Cyan));
            Logger.Info("Starting Voxa Engine v" + VERSION_MAJOR + "." + VERSION_MINOR);
            game = gameInstance;
            engineWindow = new EngineWindow(WINDOW_WIDTH, WINDOW_HEIGHT);
            renderingPool = new RenderingPool();
            uniformManager = new UniformManager();
            engineWindow.Run(TARGET_UPDATE_RATE, 0);
        }
    }
}
