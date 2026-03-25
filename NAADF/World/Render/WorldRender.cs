using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using NAADF.Common;
using NAADF.Gui.Main.Debug;
using NAADF.World.Data;
using System;

namespace NAADF.World.Render
{

    public enum RenderVersion
    {
        Albedo,
        Base,
        PathTracer
    }

    public class WorldRender : IDisposable
    {
        public static float frameTime = 0;

        public static WorldRender render = null;

        public static Camera camera;

        protected static Cube mesh;

        public int frameCount = 0, taaIndex, randCounter;
        protected int[] randValues;
        protected static float sunAngle = 60;
        protected Random rand = new Random();

        public static void ApplyRenderVersion(RenderVersion version)
        {
            render?.Dispose();
            switch (Settings.data.render.version)
            {
                case RenderVersion.Albedo: render = new WorldRenderAlbedo(); break;
                case RenderVersion.Base: render = new WorldRenderBase(); break;
                case RenderVersion.PathTracer: render = new WorldRenderPathTracer(); break;
            }
        }


        public static void Initialize()
        {
            mesh = new Cube(App.graphicsDevice);
            camera = new Camera(App.ScreenWidth / (float)App.ScreenHeight, 90, 0.1f, 10000, 0.25f);
            camera.SetPos(new Vector3(500, 200, 40));

            ApplyRenderVersion(Settings.data.render.version);
        }

        public WorldRender()
        {
            randValues = new int[32];
        }

        public void ScreenUpdate()
        {
            camera.UpdateProjection(App.ScreenWidth / (float)App.ScreenHeight, 90, 0.1f, 100000);
            CreateScreenTextures();
        }

        protected virtual void CreateScreenTextures()
        {

        }

        public void Update(float gameTime)
        {
            camera.UpdateProjection(App.ScreenWidth / (float)App.ScreenHeight, Settings.data.general.fov, 0.1f, 100000);
            camera.Update(gameTime, Settings.data.general.fov);
            float sunSpeedMul = IO.KBStates.New.IsKeyDown(Keys.LeftShift) ? 5.0f : 1.0f;
            if (IO.KBStates.New.IsKeyDown(Keys.Left))
                sunAngle -= gameTime * 0.009f * sunSpeedMul;
            if (IO.KBStates.New.IsKeyDown(Keys.Right))
                sunAngle += gameTime * 0.009f * sunSpeedMul;

            if (IO.KBStates.New.IsKeyUp(Keys.P))
            {
                for (int i = 0; i < 32; i++)
                {
                    randValues[i] = rand.Next();
                }
                frameCount++;
            }
            taaIndex = 128 - (frameCount % 128) - 1;
        }


        public void Render(WorldData data, float gameTime)
        {
            frameTime = MathHelper.Lerp(frameTime, gameTime, 0.01f);
            UiSkyDebug.skySunDir = Vector3.Transform(new Vector3(0, 1, 0), Matrix.CreateRotationZ(MathHelper.ToRadians(sunAngle)) * Matrix.CreateRotationY(MathHelper.ToRadians(30)));
            Vector3 sunColor = Atmosphere.GetLightForPoint(new Vector3(0, 10, 0));
            randCounter = 0;

            RenderInternal(data, sunColor, gameTime);

        }

        protected virtual void SelectMaterial(Point pixelPos)
        {

        }

        protected virtual void RenderInternal(WorldData data, Vector3 sunColor, float gameTime)
        {

        }

        readonly Point coprimes = new Point(3, 7);

        float Halton1D(int index, int b)
        {
            float f = 1f;
            float r = 0f;

            while (index > 0)
            {
                f /= b;
                r += f * (index % b);
                index /= b;
            }

            return r;
        }
        Vector2 Halton2D(int i, Point coprimes)
        {
            return new Vector2(
                Halton1D(i, coprimes.X),
                Halton1D(i, coprimes.Y)
            );
        }

        protected Vector2 getJitter(int frame)
        {
            return Halton2D((frame % 32) + 1, coprimes) - new Vector2(0.5f);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}
