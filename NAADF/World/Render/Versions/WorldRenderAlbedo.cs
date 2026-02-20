using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.Gui;
using NAADF.Gui.Main.Debug;
using NAADF.World.Data;
using System.Reflection.Metadata;

namespace NAADF.World.Render
{
    public class SettingDataRenderAlbedo
    {
        public bool isTAAJitter = false;
        public bool checkSun = false;
        public int taaSampleMaxAge = 1;

        public void RenderImGui()
        {
            ImGui.SliderInt("Taa max frames", ref taaSampleMaxAge, 1, 32);
            ImGuiCommon.HelperIcon("The amount of frames are used for TAA", 500);
            ImGui.Checkbox("Trace Sun", ref checkSun);
            ImGui.Checkbox("TAA Jitter", ref isTAAJitter);
        }
    }

    public class WorldRenderAlbedo : WorldRender
    {

        Effect firstHitEffect, renderTaaSample, renderFinal;

        StructuredBuffer firstHitData;

        StructuredBuffer taaSamples, taaSampleAccum;

        Matrix[] taaSampleCamTransform, taaSampleCamTransformInvers;
        PositionSplit[] oldCamPositions;
        Vector3[] taaOldCamPosFromCurCamInt;
        Vector2[] taaSampleJitter;

        public WorldRenderAlbedo() : base()
        {

            firstHitEffect = App.contentManager.Load<Effect>("shaders/render/versions/albedo/renderFirstHit");
            renderTaaSample = App.contentManager.Load<Effect>("shaders/render/versions/albedo/renderTaaSampleReverse");
            renderFinal = App.contentManager.Load<Effect>("shaders/render/versions/albedo/renderFinal");
            CreateScreenTextures();
        }

        protected override void CreateScreenTextures()
        {
            firstHitData?.Dispose();
            taaSamples?.Dispose();
            taaSampleAccum?.Dispose();

            firstHitData = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            taaSamples = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight * 32, BufferUsage.None, ShaderAccess.ReadWrite);
            taaSampleAccum = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);

            taaSampleCamTransform = new Matrix[64];
            taaSampleCamTransformInvers = new Matrix[64];
            oldCamPositions = new PositionSplit[64];
            taaSampleJitter = new Vector2[64];
            taaOldCamPosFromCurCamInt = new Vector3[64];
        }

        protected override void RenderInternal(WorldData data, Vector3 sunColor, float gameTime)
        {
            EntityHandler entityHandler = data.entityHandler;
            var settings = Settings.data.render.renderAlbedo;
            int pixelThreadGroupCount = (App.ScreenWidth * App.ScreenHeight + 63) / 64;

            Vector2 taaJitter = settings.isTAAJitter ? getJitter(frameCount) : Vector2.Zero;
            bool isTaa = settings.taaSampleMaxAge > 1 && !Settings.data.render.showSteps;

            PositionSplit camPos = camera.GetPos();
            oldCamPositions[taaIndex] = camPos;
            taaSampleCamTransform[taaIndex] = camera.viewProjTransform;
            taaSampleCamTransformInvers[taaIndex] = camera.invViewProjTransform;
            taaSampleJitter[taaIndex] = taaJitter;
            for(int i = 0; i < 64; ++i)
            {
                taaOldCamPosFromCurCamInt[i] = (oldCamPositions[i] - camPos).toVector3();
            }


            // Shoot primary rays
            firstHitEffect.setCameraPos(camPos);
            firstHitEffect.Parameters["invCamMatrix"].SetValue(camera.invViewProjTransform);
            firstHitEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            firstHitEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            firstHitEffect.Parameters["showRayStep"].SetValue(Settings.data.render.showSteps);
            firstHitEffect.Parameters["firstHitData"].SetValue(firstHitData);
            firstHitEffect.Parameters["randCounter"].SetValue(randValues[randCounter++]);
            firstHitEffect.Parameters["frameCount"].SetValue(frameCount);
            firstHitEffect.Parameters["taaJitter"].SetValue(taaJitter);
            firstHitEffect.Parameters["taaIndex"].SetValue(taaIndex);
            firstHitEffect.Parameters["taaSamples"].SetValue(taaSamples);
            firstHitEffect.Parameters["taaSampleAccum"].SetValue(taaSampleAccum);
            firstHitEffect.Parameters["sunColor"].SetValue(sunColor);
            firstHitEffect.Parameters["skySunDir"].SetValue(UiSkyDebug.skySunDir);
            firstHitEffect.Parameters["checkSun"].SetValue(settings.checkSun);
            firstHitEffect.Parameters["isTAA"].SetValue(isTaa);
            data.setEffect(firstHitEffect);

            firstHitEffect.Techniques[0].Passes["FirstHit"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);

            if (isTaa)
            {
                // Reproject previous frames for TAA
                renderTaaSample.setCameraPos(camPos);
                renderTaaSample.Parameters["camMatrix"].SetValue(camera.viewProjTransform);
                renderTaaSample.Parameters["invCamMatrix"].SetValue(camera.invViewProjTransform);
                renderTaaSample.Parameters["screenWidth"].SetValue(App.ScreenWidth);
                renderTaaSample.Parameters["screenHeight"].SetValue(App.ScreenHeight);
                renderTaaSample.Parameters["firstHitData"].SetValue(firstHitData);
                renderTaaSample.Parameters["taaSamples"].SetValue(taaSamples);
                renderTaaSample.Parameters["taaSampleAccum"].SetValue(taaSampleAccum);
                renderTaaSample.Parameters["frameCount"].SetValue(frameCount);
                renderTaaSample.Parameters["frameCount"].SetValue(frameCount);
                renderTaaSample.Parameters["sampleAge"].SetValue(settings.taaSampleMaxAge);
                renderTaaSample.Parameters["taaIndex"].SetValue(taaIndex);
                renderTaaSample.Parameters["taaJitter"].SetValue(taaJitter);
                renderTaaSample.Parameters["taaJitterOld"].SetValue(taaSampleJitter);
                renderTaaSample.Parameters["camRotOld"].SetValue(taaSampleCamTransform);
                renderTaaSample.Parameters["taaOldCamPosFromCurCamInt"].SetValue(taaOldCamPosFromCurCamInt);
                renderTaaSample.Parameters["entityInstancesHistory"]?.SetValue(entityHandler?.entityInstancesHistoryGpu);

                renderTaaSample.Techniques[0].Passes["ReprojectOld"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);
            }



            // Combine all results into final image
            renderFinal.Parameters["WorldProjection"].SetValue(Matrix.CreateTranslation(new Vector3(-0.5f)) * camera.viewProjTransform);
            renderFinal.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            renderFinal.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            renderFinal.Parameters["exposure"].SetValue(Settings.data.general.exposure);
            renderFinal.Parameters["taaSampleAccum"].SetValue(taaSampleAccum);
            renderFinal.Parameters["showRayStep"].SetValue(Settings.data.render.showSteps);
            renderFinal.CurrentTechnique.Passes[0].Apply();

            App.graphicsDevice.SetVertexBuffer(mesh);
            App.graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, mesh.VertexCount / 3);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            firstHitData?.Dispose();
            taaSamples?.Dispose();
            taaSampleAccum?.Dispose();
        }
    }
}
