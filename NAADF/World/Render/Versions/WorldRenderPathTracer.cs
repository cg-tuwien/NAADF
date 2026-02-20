using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.Gui;
using NAADF.Gui.Main.Debug;
using NAADF.World.Data;
using System.IO;

namespace NAADF.World.Render
{
    public class SettingDataRenderPathTracer
    {
        public bool isTAAJitter = true, isAtmosphereInteraction = true;
        public int maxSamples = 8192;
        public int bounceCount = 3;
        public bool anyUiChange = false;

        public void RenderImGui()
        {
            anyUiChange = false;
            anyUiChange = anyUiChange || ImGui.SliderInt("Bounces", ref bounceCount, 0, 10);
            ImGuiCommon.HelperIcon("The maximum amount of bounces for secondary rays", 500);
            ImGui.SliderInt("Max Samples", ref maxSamples, 1, 1024 * 8);
            anyUiChange = anyUiChange || ImGui.Checkbox("TAA Jitter", ref isTAAJitter);
            anyUiChange = anyUiChange || ImGui.Checkbox("Atmosphere interaction", ref isAtmosphereInteraction);
            ImGuiCommon.HelperIcon("If primary rays hit something, should atmosphere be applied. Disable to make rendering more clear", 500);
        }

    }

    public class WorldRenderPathTracer : WorldRender
    {

        int globalIlumBucketCount;
        int atmosphereTexSizeX, atmosphereTexSizeY;

        Effect firstHitEffect, globalIllumEffect, renderSky, renderFinal;

        StructuredBuffer firstHitData, firstHitAbsorption;

        StructuredBuffer finalColor, atmosphereComp, accumulatedSamples;


        public WorldRenderPathTracer() : base()
        {

            firstHitEffect = App.contentManager.Load<Effect>("shaders/render/versions/pathTracer/renderFirstHit");
            globalIllumEffect = App.contentManager.Load<Effect>("shaders/render/versions/pathTracer/renderGlobalIllum");
            renderSky = App.contentManager.Load<Effect>("shaders/render/versions/pathTracer/renderAtmosphere");
            renderFinal = App.contentManager.Load<Effect>("shaders/render/versions/pathTracer/renderFinal");
            CreateScreenTextures();
        }

        protected override void CreateScreenTextures()
        {
            

            firstHitData?.Dispose();
            firstHitAbsorption?.Dispose();
            finalColor?.Dispose();
            atmosphereComp?.Dispose();
            accumulatedSamples?.Dispose();

            atmosphereTexSizeX = 1024;
            atmosphereTexSizeY = 1024;

            firstHitData = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            firstHitAbsorption = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            finalColor = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            atmosphereComp = new StructuredBuffer(App.graphicsDevice, typeof(Uint3), atmosphereTexSizeX * atmosphereTexSizeY, BufferUsage.None, ShaderAccess.ReadWrite);
            accumulatedSamples = new StructuredBuffer(App.graphicsDevice, typeof(Vector4), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);

        }

        protected override void RenderInternal(WorldData data, Vector3 sunColor, float gameTime)
        {
            var settings = Settings.data.render.renderPathTracer;
            int pixelThreadGroupCount = (App.ScreenWidth * App.ScreenHeight + 63) / 64;

            Vector2 taaJitter = settings.isTAAJitter ? new Vector2((float)rand.NextDouble() - 0.5f, (float)rand.NextDouble() - 0.5f) : Vector2.Zero;

            PositionSplit camPos = camera.GetPos();
            int maxSamples = camera.anyViewChange || Settings.data.render.renderPathTracer.anyUiChange ? 1 : settings.maxSamples;

            // Precompute atmosphere scatter and absorption
            renderSky.Parameters["camPos"].SetValue(camera.GetPos().toVector3());
            renderSky.Parameters["atmosphereTexSizeX"].SetValue(atmosphereTexSizeX);
            renderSky.Parameters["atmosphereTexSizeY"].SetValue(atmosphereTexSizeY);
            renderSky.Parameters["atmosphereComp"].SetValue(atmosphereComp);
            renderSky.Parameters["frameCount"].SetValue(frameCount);
            UiSkyDebug.SetShaderData(renderSky);

            renderSky.Techniques[0].Passes["Atmosphere"].ApplyCompute();
            App.graphicsDevice.DispatchCompute((atmosphereTexSizeX * atmosphereTexSizeY / 4 + 63) / 64, 1, 1);


            // Shoot primary rays
            firstHitEffect.setCameraPos(camPos);
            firstHitEffect.Parameters["invCamMatrix"].SetValue(camera.invViewProjTransform);
            firstHitEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            firstHitEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            firstHitEffect.Parameters["showRayStep"].SetValue(Settings.data.render.showSteps);
            firstHitEffect.Parameters["atmosphereTexSizeX"].SetValue(atmosphereTexSizeX);
            firstHitEffect.Parameters["atmosphereTexSizeY"].SetValue(atmosphereTexSizeY);
            firstHitEffect.Parameters["atmosphereComp"].SetValue(atmosphereComp);
            firstHitEffect.Parameters["firstHitData"].SetValue(firstHitData);
            firstHitEffect.Parameters["randCounter"].SetValue(randValues[randCounter++]);
            firstHitEffect.Parameters["frameCount"].SetValue(frameCount);
            firstHitEffect.Parameters["taaJitter"].SetValue(taaJitter);
            firstHitEffect.Parameters["firstHitAbsorption"].SetValue(firstHitAbsorption);
            firstHitEffect.Parameters["finalColor"].SetValue(finalColor);
            firstHitEffect.Parameters["isAtmosphereInteraction"].SetValue(Settings.data.render.renderPathTracer.isAtmosphereInteraction);
            UiSkyDebug.SetShaderData(firstHitEffect);
            data.setEffect(firstHitEffect);

            firstHitEffect.Techniques[0].Passes["FirstHit"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);

            // Tracing secondary global illumination rays
            globalIllumEffect.setCameraPos(camPos);
            globalIllumEffect.Parameters["invCamMatrix"].SetValue(camera.invViewProjTransform);
            globalIllumEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            globalIllumEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            globalIllumEffect.Parameters["firstHitData"].SetValue(firstHitData);
            globalIllumEffect.Parameters["maxBounceCount"].SetValue(settings.bounceCount);
            globalIllumEffect.Parameters["randCounter"].SetValue(randValues[randCounter++]);
            globalIllumEffect.Parameters["frameCount"].SetValue(frameCount);
            globalIllumEffect.Parameters["taaIndex"].SetValue(taaIndex);
            globalIllumEffect.Parameters["taaJitter"].SetValue(taaJitter);
            globalIllumEffect.Parameters["sunColor"].SetValue(sunColor);
            globalIllumEffect.Parameters["firstHitAbsorption"].SetValue(firstHitAbsorption);
            globalIllumEffect.Parameters["finalColor"].SetValue(finalColor);
            globalIllumEffect.Parameters["atmosphereTexSizeX"].SetValue(atmosphereTexSizeX);
            globalIllumEffect.Parameters["atmosphereTexSizeY"].SetValue(atmosphereTexSizeY);
            globalIllumEffect.Parameters["atmosphereComp"]?.SetValue(atmosphereComp);
            globalIllumEffect.Parameters["skySunDir"].SetValue(UiSkyDebug.skySunDir);
            globalIllumEffect.Parameters["sampleAccumulated"].SetValue(accumulatedSamples);
            globalIllumEffect.Parameters["maxSamples"].SetValue(maxSamples);
            data.setEffect(globalIllumEffect);

            globalIllumEffect.Techniques[0].Passes["GlobalIlum"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);

            RenderTarget2D temp = null;
            if (App.worldHandler.pathHandler.isFinalFrame && App.worldHandler.pathHandler.screenShotAtEnd)
            {
                temp = new RenderTarget2D(App.graphicsDevice, App.ScreenWidth, App.ScreenHeight);
                App.graphicsDevice.SetRenderTarget(temp);
            }

            // Combine all results into final image
            renderFinal.Parameters["WorldProjection"].SetValue(Matrix.CreateTranslation(new Vector3(-0.5f)) * camera.viewProjTransform);
            renderFinal.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            renderFinal.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            renderFinal.Parameters["exposure"].SetValue(Settings.data.general.exposure);
            renderFinal.Parameters["sampleAccumulated"].SetValue(accumulatedSamples);
            renderFinal.Parameters["firstHitData"].SetValue(firstHitData);
            renderFinal.Parameters["showRayStep"].SetValue(Settings.data.render.showSteps);
            renderFinal.Parameters["toneMappingFac"].SetValue(Settings.data.general.toneMappingFac);
            renderFinal.CurrentTechnique.Passes[0].Apply();

            App.graphicsDevice.SetVertexBuffer(mesh);
            App.graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, mesh.VertexCount / 3);


            if (App.worldHandler.pathHandler.isFinalFrame && App.worldHandler.pathHandler.screenShotAtEnd)
            {
                App.worldHandler.pathHandler.isFinalFrame = false;
                using (var stream = new FileStream("pathFinalFrame.png", FileMode.Create))
                {
                    temp.SaveAsPng(stream, temp.Width, temp.Height);
                }
                temp.Dispose();
                App.graphicsDevice.SetRenderTarget(null);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            firstHitData?.Dispose();
            firstHitAbsorption?.Dispose();
            finalColor?.Dispose();
            atmosphereComp?.Dispose();
            accumulatedSamples?.Dispose();
        }
    }
}
