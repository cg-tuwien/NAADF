using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF
{
    public struct Mouse_States
    {
        public MouseState New, Old;

        public Mouse_States(MouseState New, MouseState Old)
        {
            this.New = New;
            this.Old = Old;
        }

        public bool IsMiddleButtonToggleOn()
        {
            return New.MiddleButton == ButtonState.Pressed && Old.MiddleButton == ButtonState.Released;
        }
        public bool IsMiddleButtonToggleOff()
        {
            return New.MiddleButton == ButtonState.Released && Old.MiddleButton == ButtonState.Pressed;
        }

        public bool IsLeftButtonToggleOn()
        {
            return New.LeftButton == ButtonState.Pressed && Old.LeftButton == ButtonState.Released;
        }
        public bool IsRightButtonToggleOn()
        {
            return New.RightButton == ButtonState.Pressed && Old.RightButton == ButtonState.Released;
        }
        public bool IsLeftButtonToggleOff()
        {
            return New.LeftButton == ButtonState.Released && Old.LeftButton == ButtonState.Pressed;
        }
        public bool IsRightButtonToggleOff()
        {
            return New.RightButton == ButtonState.Released && Old.RightButton == ButtonState.Pressed;
        }
    }

    public struct Keyboard_States
    {
        public KeyboardState New, Old;

        public Keyboard_States(KeyboardState New, KeyboardState Old)
        {
            this.New = New;
            this.Old = Old;
        }
        public bool IsKeyToggleDown(Keys key)
        {
            return New.IsKeyDown(key) && Old.IsKeyUp(key);
        }
        public bool IsKeyToggleUp(Keys key)
        {
            return New.IsKeyUp(key) && Old.IsKeyDown(key);
        }
    }

    public static class IO
    {
        public static Keyboard_States KBStates;
        public static Mouse_States MOStates;

        public static double mouseNoMoveTime = 0;

        public static void Setup()
        {
            Update(0);
            UpdateEnd();
        }

        public static void Update(double timeMs)
        {
            KBStates.New = Keyboard.GetState();
            MOStates.New = Mouse.GetState();
            if (MOStates.New.Position == MOStates.Old.Position)
                mouseNoMoveTime += timeMs;
            else
                mouseNoMoveTime = 0;
        }

        public static void UpdateEnd()
        {
            KBStates.Old = KBStates.New;
            MOStates.Old = MOStates.New;
        }
    }
}
