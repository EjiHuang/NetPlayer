using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Numerics;

namespace NetPlayer.WinUI.Win2D
{
    public static class Win2DHelper
    {
        public static Transform2DEffect CalcutateImageCenteredTransform(Vector2 cSize, Windows.Foundation.Size iSize)
        {
            return CalcutateImageCenteredTransform(cSize.X, cSize.Y, iSize.Width, iSize.Height);
        }

        public static Transform2DEffect CalcutateImageCenteredTransform(double cWidth, double cHeight, double iWidth, double iHeight)
        {
            var mat = CalcutateImageCenteredMat(cWidth, cHeight, iWidth, iHeight);
            return new Transform2DEffect() { TransformMatrix = mat };
        }

        public static Matrix3x2 CalcutateImageCenteredMat(double cWidth, double cHeight, double iWidth, double iHeight)
        {
            float f = (float)Math.Min(cWidth / iWidth, cHeight / iHeight);
            float ox = (float)(cWidth - iWidth * f) / 2;
            float oy = (float)(cHeight - iHeight * f) / 2;
            Matrix3x2 matrix3X2 = Matrix3x2.CreateScale(f) * Matrix3x2.CreateTranslation(ox, oy);
            return matrix3X2;
        }
    }
}
