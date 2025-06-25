using UnityEngine;
using Unity.Sentis;
using System;
using Unity.Collections;

public static class TextureConverter
{
      public static Tensor<float> ToTensor(Texture2D texture, ref bool isBGR)
      {
            int width = texture.width;
            int height = texture.height;
            int channels = 3;

            var tensor = new Tensor<float>(new TensorShape(1, height, width, channels));

            var length = width * height * channels;
            var managed = new byte[length];

            isBGR = false;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal)
            {
                  isBGR = true;
            }

            var data = texture.GetRawTextureData<byte>();

            if (data.Length != length)
            {
                  Debug.LogError($"Texture data length ({data.Length}) does not match expected length ({length}). Texture format might not be RGB24. Actual format: {texture.format}");
                  // Return an empty tensor or handle error appropriately to avoid further crashes.
                  return tensor;
            }

            data.CopyTo(managed);

            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        int pixelIndex = (y * width + x) * channels;
                        int tensorY = height - 1 - y; // Flip y-axis

                        if (isBGR)
                        {
                              tensor[0, tensorY, x, 0] = managed[pixelIndex + 2] / 255.0f; // R
                              tensor[0, tensorY, x, 1] = managed[pixelIndex + 1] / 255.0f; // G
                              tensor[0, tensorY, x, 2] = managed[pixelIndex + 0] / 255.0f; // B
                        }
                        else
                        {
                              tensor[0, tensorY, x, 0] = managed[pixelIndex + 0] / 255.0f; // R
                              tensor[0, tensorY, x, 1] = managed[pixelIndex + 1] / 255.0f; // G
                              tensor[0, tensorY, x, 2] = managed[pixelIndex + 2] / 255.0f; // B
                        }
                  }
            }

            return tensor;
      }

      public static void ToTexture(Tensor<float> tensor, Texture2D tex, float wallConfidence, int wallClassIndex)
      {
            var shape = tensor.shape;
            int height = shape[1];
            int width = shape[2];
            int numClasses = shape[3];

            if (wallClassIndex < 0 || wallClassIndex >= numClasses)
            {
                  Debug.LogError($"Invalid wallClassIndex: {wallClassIndex}. Must be between 0 and {numClasses - 1}");
                  return;
            }

            Color32[] pixelColors = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        byte r = 0, g = 0, b = 0, a = 0;

                        float wallProb = tensor[0, y, x, wallClassIndex];
                        if (wallProb > wallConfidence)
                        {
                              r = 255;
                              a = 255;
                        }

                        // Unity expects texture data bottom-up, but UI and other systems might be easier top-down.
                        // The output of the model is typically top-down. Let's write it out that way.
                        pixelColors[y * width + x] = new Color32(r, g, b, a);
                  }
            }

            tex.SetPixels32(pixelColors);
            tex.Apply();
      }
}