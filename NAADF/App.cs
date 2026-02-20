using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NAADF.Common;
using NAADF.Gui;
using NAADF.World;
using System;

namespace NAADF
{
    public class App : Game
    {
        private GraphicsDeviceManager graphics;
        public static SpriteBatch spriteBatch;
        public static App app;
        public static event EventHandler GraphicsChanged;
        GuiHandler guiHandler;

        public static ContentManager contentManager;
        public static GraphicsDevice graphicsDevice;
        public static System.Windows.Forms.Form form;
        public static Helper helper;

        public static int ScreenWidth;
        public static int ScreenHeight;

        private int GraphicsNeedApplyChanges = 0;

        public static WorldHandler worldHandler;

        // Gets called every time the Windows Size gets changed
        void Window_ClientSizeChanged(object sender, EventArgs e)
        {
            if (Window.ClientBounds.Width != 0)
                graphics.PreferredBackBufferWidth = Window.ClientBounds.Width;
            if (Window.ClientBounds.Height != 0)
                graphics.PreferredBackBufferHeight = Window.ClientBounds.Height;
            // Not Applying Graphics here because when resizing happens, ApplyChanges would be called too often which could cause a crash
            // When resizing happens, the Update Method is not going to be called so long until resizing is finished, and therefore Apply Changes gets only called once
            GraphicsNeedApplyChanges = 10;
        }

        public void UpdateEverythingOfGraphics(object sender, EventArgs e)
        {
            ScreenWidth = graphics.PreferredBackBufferWidth;
            ScreenHeight = graphics.PreferredBackBufferHeight;
            GraphicsDevice.PresentationParameters.RenderTargetUsage = RenderTargetUsage.DiscardContents;
            worldHandler?.ScreenUpdate();
        }

        public App()
        {
            app = this;
            GraphicsChanged += UpdateEverythingOfGraphics;
            Point maxWindowSize = new Point(GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width - 80, GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height - 160);
            graphics = new GraphicsDeviceManager(this)
            {
                GraphicsProfile = GraphicsProfile.HiDef,
                PreferredBackBufferWidth = BuildFlags.Hdr ? GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width : Math.Min(maxWindowSize.X, 1920),
                PreferredBackBufferHeight = BuildFlags.Hdr ? GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height : Math.Min(maxWindowSize.Y, 1080),
                PreferredBackBufferFormat = BuildFlags.Hdr ? SurfaceFormat.HdrBlendable : SurfaceFormat.ColorSRgb,
                IsFullScreen = BuildFlags.Hdr,
                SynchronizeWithVerticalRetrace = false,
            };
            IsFixedTimeStep = false;
            Content.RootDirectory = "Content";
            contentManager = Content;
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            form = (System.Windows.Forms.Form)System.Windows.Forms.Control.FromHandle(this.Window.Handle);
            form.Location = new System.Drawing.Point(25, 25);
            form.MaximizeBox = true;
            form.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            form.Resize += Window_ClientSizeChanged;
            GraphicsChanged(null, EventArgs.Empty);
            graphicsDevice = GraphicsDevice;
            GraphicsDevice.PresentationParameters.RenderTargetUsage = RenderTargetUsage.DiscardContents;
            guiHandler = new GuiHandler(this);
            Settings.Load();
            IO.Setup();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            helper = new Helper();
            guiHandler.SetupUi();

            worldHandler = new WorldHandler();
            worldHandler.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            IO.Update(gameTime.ElapsedGameTime.TotalMilliseconds);

            if (GraphicsNeedApplyChanges == 1)
            {
                graphics.ApplyChanges();
                GraphicsChanged(null, EventArgs.Empty);
            }
            if (GraphicsNeedApplyChanges >= 1)
                GraphicsNeedApplyChanges--;

            if (IsActive)
                worldHandler.Update((float)gameTime.ElapsedGameTime.TotalMilliseconds);

            GuiHandler.Update();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            if (IO.KBStates.IsKeyToggleDown(Keys.F11))
            {
                graphics.IsFullScreen ^= true;
                graphics.ApplyChanges();
            }

            BlendState blendState = new BlendState();
            blendState.AlphaSourceBlend = Blend.One;
            blendState.AlphaDestinationBlend = Blend.Zero;
            blendState.ColorSourceBlend = Blend.One;
            blendState.ColorDestinationBlend = Blend.Zero;
            blendState.AlphaBlendFunction = BlendFunction.Add;
            graphicsDevice.BlendState = blendState;
            GraphicsDevice.Clear(Color.CornflowerBlue);

            worldHandler.Render((float)gameTime.ElapsedGameTime.TotalMilliseconds);

            guiHandler.BeginDraw(gameTime);
            guiHandler.Draw();
            worldHandler.RenderUi();
            guiHandler.EndDraw();
            IO.UpdateEnd();
        }
    }
}