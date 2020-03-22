using NAudio.Dsp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace WPF_Version
{
    public static class DFT
    {
        public static Complex[] Compute(float[] samples)
        {
            var N = samples.Length;
            var result = new Complex[N];
            for (int k = 0; k < N; k++)
            {
                float re = 0;
                float im = 0;

                //The discrete Fourier transform, lists of Re & Im instead of a+ib
                for (int n = 0; n < N; n++)
                {
                    float phi = (2 * MathF.PI * k * n) / N;
                    re += samples[n] * MathF.Cos(phi);
                    im += - samples[n] * MathF.Sin(phi);
                }
                result[k] = new Complex() { X = re, Y = im };
            }
            return result;
        }
    }
}
