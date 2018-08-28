using UnityEngine;

namespace Pix2Pix
{
    static class GpuHelper
    {
        public static Tensor InvokeActivationKernel(string name, Tensor input)
        {
            var compute = ComputeAssets.Activation;
            var kernel = compute.FindKernel(name);

            uint tgn_x, tgn_y, tgn_z;
            compute.GetKernelThreadGroupSizes(kernel, out tgn_x, out tgn_y, out tgn_z);
            Debug.Assert(tgn_y == 1 && tgn_z == 1);

            var length = input.Buffer.count;
            Debug.Assert(length % tgn_x == 0);

            var output = new Tensor(input.Shape);
            compute.SetBuffer(kernel, "Input", input.Buffer);
            compute.SetBuffer(kernel, "Output", output.Buffer);
            compute.Dispatch(kernel, length / (int)tgn_x, 1, 1);
            return output;
        }

        public static Tensor InvokeConcatKernel(string name, Tensor input1, Tensor input2)
        {
            var compute = ComputeAssets.Concat;
            var kernel = compute.FindKernel(name);

            uint tgn_x, tgn_y, tgn_z;
            compute.GetKernelThreadGroupSizes(kernel, out tgn_x, out tgn_y, out tgn_z);
            Debug.Assert(tgn_y == 1 && tgn_z == 1);

            var height = input1.Shape[0];
            var width  = input1.Shape[1];
            var channels1 = input1.Shape[2];
            var channels2 = input2.Shape[2];

            Debug.Assert(input2.Shape[0] == height);
            Debug.Assert(input2.Shape[1] == width);
            Debug.Assert(width * height % tgn_x == 0);

            var output = new Tensor(new [] {height, width, channels1 + channels2});

            compute.SetBuffer(kernel, "Input1", input1.Buffer);
            compute.SetBuffer(kernel, "Input2", input2.Buffer);
            compute.SetBuffer(kernel, "Output", output.Buffer);

            compute.SetInts("Input1Shape", input1.Shape);
            compute.SetInts("Input2Shape", input2.Shape);

            compute.Dispatch(kernel, width * height / (int)tgn_x, 1, 1);

            return output;
        }

        public static Tensor InvokeBatchNormKernel(
            string name, Tensor input, Tensor scale, Tensor offset
        )
        {
            var compute = ComputeAssets.BatchNorm;
            var kernel = compute.FindKernel(name);

            uint tgn_x, tgn_y, tgn_z;
            compute.GetKernelThreadGroupSizes(kernel, out tgn_x, out tgn_y, out tgn_z);

            var length = input.Buffer.count;
            var channels = input.Shape[2];

            Debug.Assert(channels == tgn_x);
            Debug.Assert(channels == scale .Buffer.count);
            Debug.Assert(channels == offset.Buffer.count);

            var output = new Tensor(input.Shape);

            compute.SetInts("InputShape", input.Shape);
            compute.SetBuffer(kernel, "Input" , input .Buffer);
            compute.SetBuffer(kernel, "Scale" , scale .Buffer);
            compute.SetBuffer(kernel, "Offset", offset.Buffer);
            compute.SetBuffer(kernel, "Output", output.Buffer);
            compute.Dispatch(kernel, 1, 1, 1);

            return output;
        }

        public enum ConvolutionMode { Down, Up }

        static int[] _tempIndexVector = new int[4];

        static int[] CalculateIndexVector(int[] shape)
        {
            if (shape.Length == 4)
            {
                _tempIndexVector[0] = shape[1] * shape[2] * shape[3];
                _tempIndexVector[1] = shape[2] * shape[3];
                _tempIndexVector[2] = shape[3];
                _tempIndexVector[3] = 1;
            }
            else
            {
                _tempIndexVector[0] = shape[1] * shape[2];
                _tempIndexVector[1] = shape[2];
                _tempIndexVector[2] = 1;
                _tempIndexVector[3] = 0;
            }
            return _tempIndexVector;
        }

        public static Tensor InvokeConvolutionKernel(
            ConvolutionMode mode, string name, Tensor input, Tensor filter, Tensor bias
        )
        {
            var compute = ComputeAssets.Convolution;
            var kernel = compute.FindKernel(name);

            uint tgn_x, tgn_y, tgn_z;
            compute.GetKernelThreadGroupSizes(kernel, out tgn_x, out tgn_y, out tgn_z);

            var trans = (mode == ConvolutionMode.Up);

            var outHeight = trans ? input.Shape[0] * 2 : input.Shape[0] / 2;
            var outWidth  = trans ? input.Shape[1] * 2 : input.Shape[1] / 2;
            //var outChannels = filter.Shape[trans ? 2 : 3];
            var outChannels = filter.Shape[3];

            Debug.Assert(filter.Shape[0] == 4);
            Debug.Assert(filter.Shape[1] == 4);

            Debug.Assert(outHeight   % tgn_z == 0);
            Debug.Assert(outWidth    % tgn_y == 0);
            //Debug.Assert(outChannels % tgn_x == 0);

            var output = new Tensor(new [] {outHeight, outWidth, outChannels});

            compute.SetInts( "InputShape", input .Shape);
            compute.SetInts("FilterShape", filter.Shape);
            compute.SetInts("OutputShape", output.Shape);

            compute.SetInts( "InputIndexer", CalculateIndexVector(input .Shape));
            compute.SetInts("FilterIndexer", CalculateIndexVector(filter.Shape));
            compute.SetInts("OutputIndexer", CalculateIndexVector(output.Shape));

            compute.SetBuffer(kernel, "Input" , input .Buffer);
            compute.SetBuffer(kernel, "Filter", filter.Buffer);
            compute.SetBuffer(kernel, "Bias"  , bias  .Buffer);
            compute.SetBuffer(kernel, "Output", output.Buffer);

            compute.Dispatch(kernel,
                (trans ? input.Shape[2] : outChannels) / (int)tgn_x,
                outWidth    / (int)tgn_y,
                outHeight   / (int)tgn_z
            );

            return output;
        }

        public static Tensor SwapFilter(Tensor filter)
        {
            var compute = ComputeAssets.Convolution;
            var kernel = compute.FindKernel("SwapFilter");

            uint tgn_x, tgn_y, tgn_z;
            compute.GetKernelThreadGroupSizes(kernel, out tgn_x, out tgn_y, out tgn_z);

            var shape = filter.Shape;
            Debug.Assert(shape[0] % tgn_x == 0);
            Debug.Assert(shape[1] % tgn_y == 0);
            Debug.Assert(tgn_z == 1);

            var output = new Tensor(new [] {shape[0], shape[1], shape[3], shape[2]});

            compute.SetInts("FilterShape", filter.Shape);
            compute.SetInts("FilterIndexer", CalculateIndexVector(filter.Shape));

            compute.SetBuffer(kernel, "Filter", filter.Buffer);
            compute.SetBuffer(kernel, "Output", output.Buffer);

            compute.Dispatch(kernel, shape[0] / (int)tgn_x, shape[1] / (int)tgn_y, 1);

            return output;
        }
    }
}
