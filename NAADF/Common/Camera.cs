using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpDX.Direct3D9;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Common
{
    public struct PositionSplit
    {
        public Point3 integer;
        public Vector3 frac;

        public PositionSplit(Vector3 pos)
        {
            frac = pos;
            updateInternals();
        }

        public PositionSplit(Point3 posInt, Vector3 posFrac)
        {
            integer = posInt;
            frac = posFrac;
            updateInternals();
        }

        public Vector3 toVector3()
        {
            return integer.ToVector3() + frac;
        }

        public static PositionSplit operator +(PositionSplit value1, PositionSplit value2)
        {
            PositionSplit newPos;
            newPos.integer = value1.integer + value2.integer;
            newPos.frac = value1.frac + value2.frac;
            newPos.updateInternals();
            return newPos;
        }

        public static PositionSplit operator -(PositionSplit value1, PositionSplit value2)
        {
            PositionSplit newPos;
            newPos.integer = value1.integer - value2.integer;
            newPos.frac = value1.frac - value2.frac;
            newPos.updateInternals();
            return newPos;
        }

        public static PositionSplit operator +(PositionSplit value1)
        {
            PositionSplit newPos;
            newPos.integer = new Point3(0) - value1.integer;
            newPos.frac = Vector3.Zero - value1.frac;
            newPos.updateInternals();
            return newPos;
        }

        private void updateInternals()
        {
            Vector3 posCopy = frac;
            posCopy.Floor();
            integer += new Point3((int)posCopy.X, (int)posCopy.Y, (int)posCopy.Z);
            frac = frac - posCopy;
        }
    }

    public class Camera
    {
        private Vector3 camDir;
        private PositionSplit camPos;

        private Matrix projTransform;
        private Matrix rotationTransform;

        public Matrix viewProjTransform, invViewProjTransform, viewProjTransformWithWorld;

        float cameraSpeed;
        float curCameraSpeed = 0;
        public bool anyViewChange = false;

        Vector2 camRotation;
        private bool isCameraAiming;
        private System.Drawing.Point cursorPosAtAimStart;

        public Camera(float aspectRatio, float FOV, float nearClipPlane, float farClipPlane, float speed)
        {
            camPos = new PositionSplit(Vector3.Zero);

            rotationTransform = Matrix.CreateFromYawPitchRoll(0, 0, 0);
            UpdateProjection(aspectRatio, FOV, nearClipPlane, farClipPlane);
            cameraSpeed = speed;
        }

        public void UpdateProjection(float aspectRatio, float FOV, float nearClipPlane, float farClipPlane)
        {
            projTransform = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(FOV), aspectRatio, nearClipPlane, farClipPlane);
        }

        public void SetPos(Vector3 pos)
        {
            camPos = new PositionSplit(pos);
        }

        public PositionSplit GetPos()
        {
            return camPos;
        }

        public void SetDir(Vector3 dir)
        {
            camRotation.X = (float)Math.Atan2(dir.X, dir.Z);
            float horizontalLength = (float)Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            camRotation.Y = (float)-Math.Atan2(dir.Y, horizontalLength);
            rotationTransform = Matrix.CreateFromYawPitchRoll(camRotation.X, camRotation.Y, 0);
            camDir = Vector3.Transform(new Vector3(0, 0, 1), rotationTransform);
            camDir.Normalize();
        }

        public Vector3 GetDir()
        {
            return camDir;
        }

        public void Update(float gameTime, float fov)
        {
            anyViewChange = false;
            MouseState newState = Mouse.GetState();

            if (isCameraAiming)
            {
                float xDif = cursorPosAtAimStart.X - System.Windows.Forms.Cursor.Position.X;
                float yDif = cursorPosAtAimStart.Y - System.Windows.Forms.Cursor.Position.Y;
                xDif *= 0.002f * fov / 90.0f;
                yDif *= 0.002f * fov / 90.0f;
                camRotation.X += xDif;
                camRotation.Y -= yDif;
                camRotation.Y = Math.Clamp(camRotation.Y, -1.5f, 1.5f);
                if (xDif != 0 || yDif != 0)
                {
                    rotationTransform = Matrix.CreateFromYawPitchRoll(camRotation.X, camRotation.Y, 0);
                    System.Windows.Forms.Cursor.Position = cursorPosAtAimStart;
                }
            }

            if (newState.RightButton == ButtonState.Pressed)
            {
                anyViewChange = true;
                if (!isCameraAiming)
                    cursorPosAtAimStart = System.Windows.Forms.Cursor.Position;
                isCameraAiming = true;
            }

            if (Mouse.GetState().RightButton == ButtonState.Released && isCameraAiming)
                isCameraAiming = false;

            camDir = Vector3.Transform(new Vector3(0, 0, 1), rotationTransform);
            camDir.Normalize();


            float wantedSpeed = gameTime * (Keyboard.GetState().IsKeyDown(Keys.LeftShift) ? 1.75f : 0.15f) * cameraSpeed;
            if (Keyboard.GetState().IsKeyUp(Keys.W) && 
                Keyboard.GetState().IsKeyUp(Keys.A) && 
                Keyboard.GetState().IsKeyUp(Keys.S) && 
                Keyboard.GetState().IsKeyUp(Keys.D) &&
                Keyboard.GetState().IsKeyUp(Keys.Space) &&
                Keyboard.GetState().IsKeyUp(Keys.LeftControl))
            {
                wantedSpeed = 0;
            }
            curCameraSpeed += (wantedSpeed - curCameraSpeed) * (1 - (float)Math.Exp(-5.0f * gameTime * 0.001f));

            Vector3 camPosChange = Vector3.Zero;

            if (Keyboard.GetState().IsKeyDown(Keys.W))
                camPosChange += camDir * curCameraSpeed;
            if (Keyboard.GetState().IsKeyDown(Keys.A))
                camPosChange += (new Vector3((float)Math.Cos(camRotation.X), 0, (float)-Math.Sin(camRotation.X))) * curCameraSpeed;

            if (Keyboard.GetState().IsKeyDown(Keys.S))
                camPosChange -= camDir * curCameraSpeed;
            if (Keyboard.GetState().IsKeyDown(Keys.D))
                camPosChange += (new Vector3((float)-Math.Cos(camRotation.X), 0, (float)Math.Sin(camRotation.X))) * curCameraSpeed;

            if (Keyboard.GetState().IsKeyDown(Keys.Space))
                camPosChange.Y += curCameraSpeed;
            if (Keyboard.GetState().IsKeyDown(Keys.LeftControl))
                camPosChange.Y -= curCameraSpeed;
            camPos = camPos + new PositionSplit(camPosChange);

            if (camPosChange.X != 0 || camPosChange.Y != 0 || camPosChange.Z != 0)
                anyViewChange = true;

            Matrix viewTransform = Matrix.CreateLookAt(Vector3.Zero, camDir, Vector3.Up);
            Matrix viewTransformWithWorld = Matrix.CreateLookAt(camPos.toVector3(), camPos.toVector3() + camDir, Vector3.Up);
            viewProjTransform = viewTransform * projTransform;
            invViewProjTransform = Matrix.Invert(viewProjTransform);
            viewProjTransformWithWorld = viewTransformWithWorld * projTransform;
        }

        public Vector3 getRayDir(Point pixelPos)
        {
            Vector2 screenPosNorm = (pixelPos.ToVector2() + new Vector2(0.5f, 0.5f)) / new Vector2(App.ScreenWidth, App.ScreenHeight);
            return Vector3.Normalize(Vector3.Transform(new Vector3((screenPosNorm * 2.0f - Vector2.One) * new Vector2(1, -1), 1), invViewProjTransform));
        }
    }
}
