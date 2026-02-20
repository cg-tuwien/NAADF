using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.Gui;
using NAADF.Gui.Main.Debug;
using NAADF.World.Data;
using System;
using System.Diagnostics;
using System.IO;

namespace NAADF.World.Render
{
    public class SettingDataRenderBase
    {
        public bool isDenoise = true, isTAAJitter = true, skipSamples = true, isSampleLeveling = true, colorCorrection = true, isAtmosphereInteraction = true;
        public int taaSampleMaxAge = 32;
        public int bounceCount = 3;
        public int spatialResampleVisibilityTestMaxDepth = 80;
        public float denoiseThresh = 100;
        public int globalIllumMaxAccum = 64;
        public float spatialResampleSize = 225.0f;

        public void RenderImGui()
        {
            ImGui.SliderInt("Bounces", ref bounceCount, 0, 10);
            ImGuiCommon.HelperIcon("The maximum amount of bounces for secondary rays", 500);
            ImGui.SliderInt("Resampling max frames", ref globalIllumMaxAccum, 1, 64);
            ImGuiCommon.HelperIcon("The amount of frames being temporally acumulated for resampling", 500);
            ImGui.SliderInt("Taa max frames", ref taaSampleMaxAge, 1, 32);
            ImGuiCommon.HelperIcon("The amount of frames are used for TAA", 500);
            ImGui.SliderFloat("Resampling radius", ref spatialResampleSize, 2, 500, "%.5g", ImGuiSliderFlags.Logarithmic);
            ImGuiCommon.HelperIcon("The radius used for selecting near samples in resampling", 500);
            ImGui.SliderInt("Spatial max visibility", ref spatialResampleVisibilityTestMaxDepth, 0, 80);
            ImGuiCommon.HelperIcon("The max amount of ray steps for resampling visibility testing. Lower values can improve performance but cause light leaks", 500);
            ImGui.SliderFloat("Denoise Threshold", ref denoiseThresh, 0.1f, 500.0f, "%.5g", ImGuiSliderFlags.Logarithmic);
            ImGuiCommon.HelperIcon("The weighting factor for the bilateral filter", 500);
            ImGui.Checkbox("Color correction", ref colorCorrection);
            ImGuiCommon.HelperIcon("Mitigates some of the darkening from resampling", 500);
            ImGui.Checkbox("TAA Jitter", ref isTAAJitter);
            ImGui.Checkbox("Sample leveling", ref isSampleLeveling);
            ImGuiCommon.HelperIcon("Performs brightness leveling in each region reducing noise", 500);
            ImGui.Checkbox("Skip samples", ref skipSamples);
            ImGuiCommon.HelperIcon("Toggles between 1spp and 0.25 spp", 500);
            ImGui.Checkbox("Denoise", ref isDenoise);
            ImGui.Checkbox("Atmosphere interaction", ref isAtmosphereInteraction);
            ImGuiCommon.HelperIcon("If primary rays hit something, should atmosphere be applied. Disable to make rendering more clear", 500);
        }
    }

    public class WorldRenderBase : WorldRender
    {
        public static readonly int globalIllumValidSampleStorageCount = 2;
        public static readonly int globalIllumInvalidSampleStorageCount = 4;
        public static readonly int globalIllumBucketStorageCount = 32;
        public static readonly int globalIllumRefinedBucketStorageCount = 8;

        int globalIlumBucketCount;
        int atmosphereTexSizeX, atmosphereTexSizeY;

        Effect firstHitEffect, rayQueueEffect, globalIllumEffect, sampleRefineEffect, spatialResamplingEffect, denoiseEffect, renderSky, renderTaa, renderTaaSample, renderFinal;

        StructuredBuffer rayQueueBuffer;
        IndirectDrawBuffer rayQueueIndirectBuffer;

        StructuredBuffer firstHitData, firstHitAbsorption;

        StructuredBuffer globalIlumValidSamples, globalIlumValidSamplesRefined, globalIlumValidSamplesCompressed, globalIlumInvalidSamples, globalIlumSampleCounts;
        StructuredBuffer globalIlumBucketInfo;
        IndirectDrawBuffer globalIlumValidDispatch, globalIlumInvalidDispatch;

        StructuredBuffer finalColor, atmosphereComp;

        StructuredBuffer denoisePreprocessed, denoisePreprocessedHorizontal;

        StructuredBuffer taaSamples, taaSampleAccum, taaDistMinMax;

        Matrix[] taaSampleCamTransform, taaSampleCamTransformInvers;
        Matrix oldCamTransformWithWorld;
        Vector3[] taaOldCamPosFromCurCamInt;
        Vector2[] taaSampleJitter;
        PositionSplit[] oldCamPositions;


        public WorldRenderBase() : base()
        {

            firstHitEffect = App.contentManager.Load<Effect>("shaders/render/versions/base/renderFirstHit");
            globalIllumEffect = App.contentManager.Load<Effect>("shaders/render/versions/base/renderGlobalIllum");
            sampleRefineEffect = App.contentManager.Load<Effect>("shaders/render/versions/base/renderSampleRefine");
            spatialResamplingEffect = App.contentManager.Load<Effect>("shaders/render/versions/base/renderSpatialResampling");
            renderSky = App.contentManager.Load<Effect>("shaders/render/versions/base/renderAtmosphere");
            denoiseEffect = App.contentManager.Load<Effect>("shaders/render/versions/base/renderDenoiseSplit");
            rayQueueEffect = App.contentManager.Load<Effect>("shaders/render/versions/base/rayQueueCalc");
            renderTaaSample = App.contentManager.Load<Effect>("shaders/render/versions/base/renderTaaSampleReverse");
            renderFinal = App.contentManager.Load<Effect>("shaders/render/versions/base/renderFinal");
            CreateScreenTextures();
        }

        protected override void CreateScreenTextures()
        {
            rayQueueBuffer?.Dispose();
            rayQueueIndirectBuffer?.Dispose();
            firstHitData?.Dispose();
            firstHitAbsorption?.Dispose();
            finalColor?.Dispose();
            atmosphereComp?.Dispose();

            globalIlumBucketInfo?.Dispose();
            globalIlumValidSamples?.Dispose();
            globalIlumValidSamplesCompressed?.Dispose();
            globalIlumValidSamplesRefined?.Dispose();
            globalIlumInvalidSamples?.Dispose();
            globalIlumSampleCounts?.Dispose();
            globalIlumValidDispatch?.Dispose();
            globalIlumInvalidDispatch?.Dispose();

            denoisePreprocessed?.Dispose();
            denoisePreprocessedHorizontal?.Dispose();

            taaSamples?.Dispose();
            taaSampleAccum?.Dispose();
            taaDistMinMax?.Dispose();



            atmosphereTexSizeX = 1024;
            atmosphereTexSizeY = 1024;

            rayQueueBuffer = new StructuredBuffer(App.graphicsDevice, typeof(uint), App.ScreenWidth * App.ScreenHeight + 1, BufferUsage.None, ShaderAccess.ReadWrite);
            rayQueueIndirectBuffer = new IndirectDrawBuffer(App.graphicsDevice, BufferUsage.None, ShaderAccess.ReadWrite, 5);
            rayQueueIndirectBuffer.SetData(new DispatchComputeArguments { GroupCountX = 0, GroupCountY = 1, GroupCountZ = 1 });

            firstHitData = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            firstHitAbsorption = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            finalColor = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            atmosphereComp = new StructuredBuffer(App.graphicsDevice, typeof(Uint3), atmosphereTexSizeX * atmosphereTexSizeY, BufferUsage.None, ShaderAccess.ReadWrite);

            denoisePreprocessed = new StructuredBuffer(App.graphicsDevice, typeof(Uint3), App.ScreenWidth * App.ScreenHeight * 1, BufferUsage.None, ShaderAccess.ReadWrite);
            denoisePreprocessedHorizontal = new StructuredBuffer(App.graphicsDevice, typeof(Uint3), App.ScreenWidth * App.ScreenHeight * 1, BufferUsage.None, ShaderAccess.ReadWrite);

            taaSamples = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight * 32, BufferUsage.None, ShaderAccess.ReadWrite);
            taaSampleAccum = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);
            taaDistMinMax = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), App.ScreenWidth * App.ScreenHeight, BufferUsage.None, ShaderAccess.ReadWrite);

            taaSampleCamTransform = new Matrix[64];
            taaSampleCamTransformInvers = new Matrix[64];
            taaOldCamPosFromCurCamInt = new Vector3[64];
            taaSampleJitter = new Vector2[64];
            oldCamPositions = new PositionSplit[64];

            // Global Illumination
            int globalIlumBucketSizeX = (App.ScreenWidth + 7) / 8;
            int globalIlumBucketSizeY = (App.ScreenHeight + 7) / 8;
            globalIlumBucketCount = globalIlumBucketSizeX * globalIlumBucketSizeY;
            globalIlumBucketInfo = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), globalIlumBucketCount, BufferUsage.None, ShaderAccess.ReadWrite);
            globalIlumValidSamples = new StructuredBuffer(App.graphicsDevice, typeof(Uint8), App.ScreenWidth * App.ScreenHeight * globalIllumValidSampleStorageCount, BufferUsage.None, ShaderAccess.ReadWrite);
            globalIlumValidSamplesCompressed = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), globalIlumBucketSizeX * globalIlumBucketSizeY * globalIllumRefinedBucketStorageCount, BufferUsage.None, ShaderAccess.ReadWrite);
            globalIlumValidSamplesRefined = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), globalIlumBucketSizeX * globalIlumBucketSizeY * globalIllumBucketStorageCount, BufferUsage.None, ShaderAccess.ReadWrite);
            globalIlumInvalidSamples = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), App.ScreenWidth * App.ScreenHeight * globalIllumInvalidSampleStorageCount, BufferUsage.None, ShaderAccess.ReadWrite);
            globalIlumSampleCounts = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), 64 + 3, BufferUsage.None, ShaderAccess.ReadWrite);

            globalIlumValidDispatch = new IndirectDrawBuffer(App.graphicsDevice, BufferUsage.None, ShaderAccess.ReadWrite, 5);
            globalIlumValidDispatch.SetData(new DispatchComputeArguments { GroupCountX = 1, GroupCountY = 1, GroupCountZ = 1 });
            globalIlumInvalidDispatch = new IndirectDrawBuffer(App.graphicsDevice, BufferUsage.None, ShaderAccess.ReadWrite, 5);
            globalIlumInvalidDispatch.SetData(new DispatchComputeArguments { GroupCountX = 1, GroupCountY = 1, GroupCountZ = 1 });
        }

        protected override void RenderInternal(WorldData data, Vector3 sunColor, float gameTime)
        {
            EntityHandler entityHandler = data.entityHandler;
            var settings = Settings.data.render.renderBase;
            int pixelThreadGroupCount = (App.ScreenWidth * App.ScreenHeight + 63) / 64;

            int globalIlumBucketSizeX = (App.ScreenWidth + 7) / 8;
            int globalIlumBucketSizeY = (App.ScreenHeight + 7) / 8;
            int globalIlumAccumIndex = settings.globalIllumMaxAccum - (frameCount % settings.globalIllumMaxAccum) - 1;

            Vector2 taaJitter = settings.isTAAJitter ? getJitter(frameCount) : Vector2.Zero;

            int taaIndexOld = (taaIndex + 1) % 64;
            PositionSplit camPos = camera.GetPos();
            oldCamPositions[taaIndex] = camPos;
            taaSampleCamTransform[taaIndex] = camera.viewProjTransform;
            taaSampleCamTransformInvers[taaIndex] = camera.invViewProjTransform;
            taaSampleJitter[taaIndex] = taaJitter;
            for (int i = 0; i < 64; ++i)
            {
                taaOldCamPosFromCurCamInt[i] = (oldCamPositions[i] - camPos).toVector3();
            }


            // Precompute atmosphere scatter and absorption
            renderSky.Parameters["camPos"].SetValue(camPos.toVector3());
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
            firstHitEffect.Parameters["isAtmosphereInteraction"].SetValue(Settings.data.render.renderBase.isAtmosphereInteraction);
            UiSkyDebug.SetShaderData(firstHitEffect);
            data.setEffect(firstHitEffect);

            firstHitEffect.Techniques[0].Passes["FirstHit"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);

            // Reproject previous frames for TAA
            renderTaaSample.setCameraPos(camPos);
            renderTaaSample.Parameters["camMatrix"].SetValue(camera.viewProjTransform);
            renderTaaSample.Parameters["invCamMatrix"].SetValue(camera.invViewProjTransform);
            renderTaaSample.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            renderTaaSample.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            renderTaaSample.Parameters["firstHitData"].SetValue(firstHitData);
            renderTaaSample.Parameters["taaSamples"].SetValue(taaSamples);
            renderTaaSample.Parameters["taaSampleAccum"].SetValue(taaSampleAccum);
            renderTaaSample.Parameters["taaDistMinMax"].SetValue(taaDistMinMax);
            renderTaaSample.Parameters["frameCount"].SetValue(frameCount);
            renderTaaSample.Parameters["sampleAge"].SetValue(settings.taaSampleMaxAge);
            renderTaaSample.Parameters["taaIndex"].SetValue(taaIndex);
            renderTaaSample.Parameters["taaJitter"].SetValue(taaJitter);
            renderTaaSample.Parameters["taaJitterOld"].SetValue(taaSampleJitter);
            renderTaaSample.Parameters["camRotOld"].SetValue(taaSampleCamTransform);
            renderTaaSample.Parameters["taaOldCamPosFromCurCamInt"].SetValue(taaOldCamPosFromCurCamInt);
            renderTaaSample.Parameters["finalColor"].SetValue(finalColor);
            renderTaaSample.Parameters["voxelTypeData"].SetValue(App.worldHandler.voxelTypeHandler.typesRenderGpu.GetBuffer());
            renderTaaSample.Parameters["entityInstancesHistory"]?.SetValue(entityHandler?.entityInstancesHistoryGpu);

            renderTaaSample.Techniques[0].Passes["ReprojectOld"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);

            Stopwatch watch = new Stopwatch();

            // Prepare sample buckets for spatial resampling
            sampleRefineEffect.Parameters["firstHitData"].SetValue(firstHitData);
            sampleRefineEffect.Parameters["accumIndex"].SetValue(globalIlumAccumIndex);
            sampleRefineEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            sampleRefineEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            sampleRefineEffect.Parameters["sampleMaxAccum"].SetValue(settings.globalIllumMaxAccum);
            sampleRefineEffect.Parameters["validSampleStorageCount"].SetValue(globalIllumValidSampleStorageCount);
            sampleRefineEffect.Parameters["invalidSampleStorageCount"].SetValue(globalIllumInvalidSampleStorageCount);
            sampleRefineEffect.Parameters["bucketStorageCount"].SetValue(globalIllumBucketStorageCount);
            sampleRefineEffect.Parameters["refinedBucketStorageCount"].SetValue(globalIllumRefinedBucketStorageCount);
            sampleRefineEffect.Parameters["globalIlumSampleCounts"].SetValue(globalIlumSampleCounts);
            sampleRefineEffect.Parameters["globalIlumBucketInfo"].SetValue(globalIlumBucketInfo);
            sampleRefineEffect.Parameters["globalIlumBucketCount"].SetValue(globalIlumBucketCount);
            sampleRefineEffect.Parameters["groupCount"].SetValue(rayQueueIndirectBuffer);

            sampleRefineEffect.Techniques[0].Passes["ClearBucketsAndCalcMask"].ApplyCompute();
            App.graphicsDevice.DispatchCompute((globalIlumBucketCount + 63) / 64, 1, 1);

            // Compute for which pixels secondary rays should be cast
            rayQueueEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            rayQueueEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            rayQueueEffect.Parameters["frameIndex"].SetValue(frameCount);
            rayQueueEffect.Parameters["firstHitData"].SetValue(firstHitData);
            rayQueueEffect.Parameters["groupCount"].SetValue(rayQueueIndirectBuffer);
            rayQueueEffect.Parameters["pixelsToRender"].SetValue(rayQueueBuffer);
            rayQueueEffect.Parameters["taaSampleAccum"]?.SetValue(taaSampleAccum);
            rayQueueEffect.Parameters["skipSamples"].SetValue(settings.skipSamples);

            rayQueueEffect.Techniques[0].Passes["RayQueue"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);
            rayQueueEffect.Techniques[0].Passes["RayQueueStore"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(1, 1, 1);


            globalIllumEffect.Parameters["taaJitterOld"].SetValue(taaSampleJitter);
            globalIllumEffect.Parameters["camRotOld"].SetValue(taaSampleCamTransform);
            globalIllumEffect.Parameters["taaOldCamPosFromCurCamInt"].SetValue(taaOldCamPosFromCurCamInt);
            globalIllumEffect.Parameters["invCamMatrix"].SetValue(camera.invViewProjTransform);
            globalIllumEffect.setCameraPos(camPos);
            globalIllumEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            globalIllumEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            globalIllumEffect.Parameters["pixelsToRender"].SetValue(rayQueueBuffer);
            globalIllumEffect.Parameters["firstHitData"].SetValue(firstHitData);
            globalIllumEffect.Parameters["maxBounceCount"].SetValue(settings.bounceCount);
            globalIllumEffect.Parameters["randCounter"].SetValue(randValues[randCounter++]);
            globalIllumEffect.Parameters["frameCount"].SetValue(frameCount);
            globalIllumEffect.Parameters["globalIlumValidSamples"].SetValue(globalIlumValidSamples);
            globalIllumEffect.Parameters["globalIlumInvalidSamples"].SetValue(globalIlumInvalidSamples);
            globalIllumEffect.Parameters["globalIlumSampleCounts"].SetValue(globalIlumSampleCounts);
            globalIllumEffect.Parameters["validSampleStorageCount"].SetValue(globalIllumValidSampleStorageCount);
            globalIllumEffect.Parameters["invalidSampleStorageCount"].SetValue(globalIllumInvalidSampleStorageCount);
            globalIllumEffect.Parameters["accumIndex"].SetValue(globalIlumAccumIndex);
            globalIllumEffect.Parameters["taaIndex"].SetValue(taaIndex);
            globalIllumEffect.Parameters["taaJitter"].SetValue(taaJitter);
            globalIllumEffect.Parameters["sunColor"].SetValue(sunColor);
            globalIllumEffect.Parameters["firstHitAbsorption"]?.SetValue(firstHitAbsorption);
            globalIllumEffect.Parameters["atmosphereTexSizeX"].SetValue(atmosphereTexSizeX);
            globalIllumEffect.Parameters["atmosphereTexSizeY"].SetValue(atmosphereTexSizeY);
            globalIllumEffect.Parameters["atmosphereComp"]?.SetValue(atmosphereComp);
            globalIllumEffect.Parameters["skySunDir"].SetValue(UiSkyDebug.skySunDir);
            globalIllumEffect.Parameters["entityInstancesHistory"]?.SetValue(entityHandler?.entityInstancesHistoryGpu);
            data.setEffect(globalIllumEffect);

            // Tracing secondary global illumination rays
            globalIllumEffect.Techniques[0].Passes["GlobalIlum"].ApplyCompute();
            App.graphicsDevice.DispatchComputeIndirect(rayQueueIndirectBuffer);


            

            // Refine samples from last frames into 8x8 buckets
            sampleRefineEffect.Parameters["randCounter"].SetValue(randValues[randCounter++]);
            sampleRefineEffect.Parameters["randCounter2"].SetValue(randValues[randCounter++]);
            sampleRefineEffect.Parameters["globalIlumBucketSizeX"].SetValue(globalIlumBucketSizeX);
            sampleRefineEffect.Parameters["globalIlumBucketSizeY"].SetValue(globalIlumBucketSizeY);
            sampleRefineEffect.Parameters["globalIlumBucketCount"].SetValue(globalIlumBucketCount);
            sampleRefineEffect.setCameraPos(camPos);
            sampleRefineEffect.Parameters["camMatrix"].SetValue(camera.viewProjTransform);
            sampleRefineEffect.Parameters["globalIlumValidDispatch"].SetValue(globalIlumValidDispatch);
            sampleRefineEffect.Parameters["globalIlumInvalidDispatch"].SetValue(globalIlumInvalidDispatch);
            sampleRefineEffect.Parameters["globalIlumSampleCounts"].SetValue(globalIlumSampleCounts);
            sampleRefineEffect.Parameters["globalIlumBucketInfo"].SetValue(globalIlumBucketInfo);
            sampleRefineEffect.Parameters["globalIlumValidSamples"].SetValue(globalIlumValidSamples);
            sampleRefineEffect.Parameters["globalIlumValidSamplesRefined"].SetValue(globalIlumValidSamplesRefined);
            sampleRefineEffect.Parameters["globalIlumValidSamplesCompressed"].SetValue(globalIlumValidSamplesCompressed);
            sampleRefineEffect.Parameters["globalIlumInvalidSamples"].SetValue(globalIlumInvalidSamples);
            sampleRefineEffect.Parameters["taaDistMinMax"]?.SetValue(taaDistMinMax);
            sampleRefineEffect.Parameters["taaIndex"].SetValue(taaIndex);
            sampleRefineEffect.Parameters["camRotOld"].SetValue(taaSampleCamTransformInvers);
            sampleRefineEffect.Parameters["taaOldCamPosFromCurCamInt"].SetValue(taaOldCamPosFromCurCamInt);
            sampleRefineEffect.Parameters["isSampleLeveling"].SetValue(settings.isSampleLeveling);
            sampleRefineEffect.Parameters["entityInstancesHistory"]?.SetValue(entityHandler?.entityInstancesHistoryGpu);

            sampleRefineEffect.Techniques[0].Passes["ValidHistory"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(1, 1, 1);

            sampleRefineEffect.Techniques[0].Passes["CountValidAndRefine"].ApplyCompute();
            App.graphicsDevice.DispatchComputeIndirect(globalIlumValidDispatch);

            sampleRefineEffect.Techniques[0].Passes["CountInvalid"].ApplyCompute();
            App.graphicsDevice.DispatchComputeIndirect(globalIlumInvalidDispatch);

            sampleRefineEffect.Techniques[0].Passes["RefineBuckets"].ApplyCompute();
            App.graphicsDevice.DispatchCompute((globalIlumBucketCount + 63) / 64, 1, 1);

            // Perform spatial resampling
            data.setEffect(spatialResamplingEffect);
            spatialResamplingEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
            spatialResamplingEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
            spatialResamplingEffect.Parameters["globalIlumBucketSizeX"].SetValue(globalIlumBucketSizeX);
            spatialResamplingEffect.Parameters["globalIlumBucketSizeY"].SetValue(globalIlumBucketSizeY);
            spatialResamplingEffect.Parameters["firstHitData"].SetValue(firstHitData);
            spatialResamplingEffect.Parameters["globalIlumValidSamplesCompressed"]?.SetValue(globalIlumValidSamplesCompressed);
            spatialResamplingEffect.Parameters["globalIlumBucketInfo"]?.SetValue(globalIlumBucketInfo);
            spatialResamplingEffect.Parameters["randCounter"].SetValue(randValues[randCounter++]);
            spatialResamplingEffect.Parameters["test1"].SetValue(settings.spatialResampleSize);
            spatialResamplingEffect.Parameters["bucketStorageCount"].SetValue(globalIllumBucketStorageCount);
            spatialResamplingEffect.Parameters["sunColor"].SetValue(sunColor);
            spatialResamplingEffect.Parameters["colorCorrection"].SetValue(settings.colorCorrection);

            spatialResamplingEffect.Parameters["spatialVisibilityCount"].SetValue(settings.spatialResampleVisibilityTestMaxDepth);
            spatialResamplingEffect.setCameraPos(camPos);
            spatialResamplingEffect.Parameters["taaSampleAccum"].SetValue(taaSampleAccum);

            spatialResamplingEffect.Parameters["skySunDir"].SetValue(UiSkyDebug.skySunDir);

            spatialResamplingEffect.Parameters["invCamMatrix"].SetValue(camera.invViewProjTransform);
            spatialResamplingEffect.Parameters["frameCount"].SetValue(frameCount);
            spatialResamplingEffect.Parameters["taaIndex"].SetValue(taaIndex);
            spatialResamplingEffect.Parameters["taaJitter"].SetValue(taaJitter);
            spatialResamplingEffect.Parameters["denoisePreprocessed"].SetValue(denoisePreprocessed);
            spatialResamplingEffect.Parameters["isDenoise"].SetValue(settings.isDenoise);
            spatialResamplingEffect.Parameters["firstHitAbsorption"].SetValue(firstHitAbsorption);
            spatialResamplingEffect.Parameters["finalColor"]?.SetValue(finalColor);
            spatialResamplingEffect.Parameters["camRotOld"].SetValue(taaSampleCamTransform[(taaIndex + 1) % 64]);
            spatialResamplingEffect.Parameters["entityInstancesHistory"]?.SetValue(entityHandler?.entityInstancesHistoryGpu);

            spatialResamplingEffect.Techniques[0].Passes["SpatialResampling"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);

            // Denoise
            if (settings.isDenoise)
            {
                denoiseEffect.Parameters["screenWidth"].SetValue(App.ScreenWidth);
                denoiseEffect.Parameters["screenHeight"].SetValue(App.ScreenHeight);
                denoiseEffect.Parameters["firstHitAbsorption"].SetValue(firstHitAbsorption);
                denoiseEffect.Parameters["randCounter"].SetValue(randValues[randCounter++]);
                denoiseEffect.Parameters["denoiseThresh"].SetValue(settings.denoiseThresh);
                denoiseEffect.Parameters["denoisePreprocessed"].SetValue(denoisePreprocessed);
                denoiseEffect.Parameters["denoisePreprocessedHorizontal"].SetValue(denoisePreprocessedHorizontal);
                denoiseEffect.Parameters["finalColor"].SetValue(finalColor);
                denoiseEffect.Parameters["entityInstancesHistory"]?.SetValue(entityHandler?.entityInstancesHistoryGpu);

                denoiseEffect.Techniques[0].Passes["CalcDenoiseHorizontal"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);

                denoiseEffect.Techniques[0].Passes["CalcDenoiseVertical"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(pixelThreadGroupCount, 1, 1);
            }


            // Add new colors to TAA result
            renderTaaSample.Techniques[0].Passes["CalcNewTaaSample"].ApplyCompute();
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
            renderFinal.Parameters["taaSampleAccum"]?.SetValue(taaSampleAccum);
            renderFinal.Parameters["toneMappingFac"]?.SetValue(Settings.data.general.toneMappingFac);
            renderFinal.Parameters["firstHitData"].SetValue(firstHitData);
            renderFinal.Parameters["showRayStep"].SetValue(Settings.data.render.showSteps);
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

            oldCamTransformWithWorld = camera.viewProjTransformWithWorld;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            rayQueueBuffer?.Dispose();
            rayQueueIndirectBuffer?.Dispose();
            firstHitData?.Dispose();
            firstHitAbsorption?.Dispose();
            finalColor?.Dispose();
            atmosphereComp?.Dispose();

            globalIlumBucketInfo?.Dispose();
            globalIlumValidSamples?.Dispose();
            globalIlumValidSamplesCompressed?.Dispose();
            globalIlumValidSamplesRefined?.Dispose();
            globalIlumInvalidSamples?.Dispose();
            globalIlumSampleCounts?.Dispose();
            globalIlumValidDispatch?.Dispose();
            globalIlumInvalidDispatch?.Dispose();

            denoisePreprocessed?.Dispose();
            denoisePreprocessedHorizontal?.Dispose();

            taaSamples?.Dispose();
            taaSampleAccum?.Dispose();
            taaDistMinMax?.Dispose();
        }
    }
}
