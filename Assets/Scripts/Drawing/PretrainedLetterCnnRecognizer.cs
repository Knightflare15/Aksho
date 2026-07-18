using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class PretrainedLetterCnnRecognizer
{
    const int InputSize = 28;
    const int InputPixels = InputSize * InputSize;
    const int ClassCount = 47;
    const float BinaryThreshold = 0.32f;
    const string DefaultResourcePath = "LetterCnn/emnist_balanced_lenet";

    LetterCnnWeights weights;

    public bool IsAvailable => weights != null;
    public string ModelName { get; private set; } = "Hugging Face LeNet-5 EMNIST Balanced CNN";

    public static PretrainedLetterCnnRecognizer CreateDefault()
    {
        var recognizer = new PretrainedLetterCnnRecognizer();
        recognizer.TryLoadFromResources(DefaultResourcePath);
        return recognizer;
    }

    public bool TryLoadFromResources(string resourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null || asset.bytes == null || asset.bytes.Length == 0)
            return false;

        try
        {
            weights = LetterCnnWeights.Read(asset.bytes);
            return weights != null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PretrainedLetterCnnRecognizer] Could not load CNN weights '{resourcePath}': {ex.Message}");
            weights = null;
            return false;
        }
    }

    public TinyLetterNeuralRecognizer.NeuralResult RecognizeImage(List<List<Vector2>> strokes)
    {
        if (!IsAvailable || CountPoints(strokes) < 2)
            return EmptyResult();

        float[] input = RasterizeInput(strokes);
        float[] probabilities = Predict(input);

        int bestClass = -1;
        int runnerUpClass = -1;
        float bestProbability = 0f;
        float runnerUpProbability = 0f;

        for (int classId = 10; classId <= 35; classId++)
        {
            float probability = probabilities[classId];
            if (probability > bestProbability)
            {
                runnerUpProbability = bestProbability;
                runnerUpClass = bestClass;
                bestProbability = probability;
                bestClass = classId;
            }
            else if (probability > runnerUpProbability)
            {
                runnerUpProbability = probability;
                runnerUpClass = classId;
            }
        }

        if (bestClass < 0)
            return EmptyResult();

        string bestName = ClassToUppercaseLetter(bestClass);
        string runnerUpName = runnerUpClass >= 0 ? ClassToUppercaseLetter(runnerUpClass) : "Unknown";
        float margin = bestProbability - runnerUpProbability;

        return new TinyLetterNeuralRecognizer.NeuralResult
        {
            name = bestName,
            confidence = Mathf.Clamp01(bestProbability * 0.72f + Mathf.Clamp01(margin * 4.5f) * 0.28f),
            runnerUpName = runnerUpName,
            runnerUpConfidence = runnerUpProbability,
            isAmbiguous = margin < 0.10f,
        };
    }

    float[] Predict(float[] input)
    {
        float[] conv1 = Conv2D(
            input,
            InputSize,
            InputSize,
            1,
            weights.conv1Kernel,
            weights.conv1Bias,
            5,
            6,
            true);
        Relu(conv1);

        float[] pool1 = AveragePool2X2(conv1, 28, 28, 6);

        float[] conv2 = Conv2D(
            pool1,
            14,
            14,
            6,
            weights.conv2Kernel,
            weights.conv2Bias,
            5,
            16,
            false);
        Relu(conv2);

        float[] pool2 = AveragePool2X2(conv2, 10, 10, 16);
        float[] dense1 = Dense(pool2, weights.dense1Kernel, weights.dense1Bias, 400, 120);
        Relu(dense1);

        float[] dense2 = Dense(dense1, weights.dense2Kernel, weights.dense2Bias, 120, 84);
        Relu(dense2);

        float[] logits = Dense(dense2, weights.dense3Kernel, weights.dense3Bias, 84, ClassCount);
        SoftmaxInPlace(logits);
        return logits;
    }

    static float[] Conv2D(
        float[] input,
        int width,
        int height,
        int inputChannels,
        float[] kernel,
        float[] bias,
        int kernelSize,
        int outputChannels,
        bool samePadding)
    {
        int padding = samePadding ? kernelSize / 2 : 0;
        int outputWidth = samePadding ? width : width - kernelSize + 1;
        int outputHeight = samePadding ? height : height - kernelSize + 1;
        var output = new float[outputWidth * outputHeight * outputChannels];

        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                for (int outputChannel = 0; outputChannel < outputChannels; outputChannel++)
                {
                    float sum = bias[outputChannel];
                    for (int ky = 0; ky < kernelSize; ky++)
                    {
                        int iy = y + ky - padding;
                        if (iy < 0 || iy >= height)
                            continue;

                        for (int kx = 0; kx < kernelSize; kx++)
                        {
                            int ix = x + kx - padding;
                            if (ix < 0 || ix >= width)
                                continue;

                            for (int inputChannel = 0; inputChannel < inputChannels; inputChannel++)
                            {
                                int inputIndex = ((iy * width) + ix) * inputChannels + inputChannel;
                                int kernelIndex = (((ky * kernelSize + kx) * inputChannels + inputChannel) * outputChannels) + outputChannel;
                                sum += input[inputIndex] * kernel[kernelIndex];
                            }
                        }
                    }

                    output[((y * outputWidth) + x) * outputChannels + outputChannel] = sum;
                }
            }
        }

        return output;
    }

    static float[] AveragePool2X2(float[] input, int width, int height, int channels)
    {
        int outputWidth = width / 2;
        int outputHeight = height / 2;
        var output = new float[outputWidth * outputHeight * channels];

        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    float sum = 0f;
                    for (int py = 0; py < 2; py++)
                    {
                        for (int px = 0; px < 2; px++)
                        {
                            int inputIndex = ((((y * 2) + py) * width) + ((x * 2) + px)) * channels + channel;
                            sum += input[inputIndex];
                        }
                    }

                    output[((y * outputWidth) + x) * channels + channel] = sum * 0.25f;
                }
            }
        }

        return output;
    }

    static float[] Dense(float[] input, float[] kernel, float[] bias, int inputCount, int outputCount)
    {
        var output = new float[outputCount];
        for (int outputIndex = 0; outputIndex < outputCount; outputIndex++)
        {
            float sum = bias[outputIndex];
            for (int inputIndex = 0; inputIndex < inputCount; inputIndex++)
                sum += input[inputIndex] * kernel[inputIndex * outputCount + outputIndex];
            output[outputIndex] = sum;
        }
        return output;
    }

    static void Relu(float[] values)
    {
        for (int i = 0; i < values.Length; i++)
            if (values[i] < 0f)
                values[i] = 0f;
    }

    static void SoftmaxInPlace(float[] values)
    {
        float max = float.NegativeInfinity;
        for (int i = 0; i < values.Length; i++)
            if (values[i] > max)
                max = values[i];

        float sum = 0f;
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Mathf.Exp(values[i] - max);
            sum += values[i];
        }

        if (sum <= 0f)
            return;

        for (int i = 0; i < values.Length; i++)
            values[i] /= sum;
    }

    static float[] RasterizeInput(List<List<Vector2>> strokes)
    {
        Bounds2D bounds = MeasureBounds(strokes);
        float[] ink = RenderInkToNormalizedSquare(strokes, bounds, InputSize, 1.85f);
        var input = new float[InputPixels];
        for (int i = 0; i < input.Length; i++)
            input[i] = ink[i] >= BinaryThreshold ? 1f : Mathf.Clamp01(ink[i]);
        return input;
    }

    static float[] RenderInkToNormalizedSquare(
        List<List<Vector2>> strokes,
        Bounds2D bounds,
        int size,
        float brushRadius)
    {
        float[] ink = new float[size * size];
        float width = Mathf.Max(1f, bounds.maxX - bounds.minX);
        float height = Mathf.Max(1f, bounds.maxY - bounds.minY);
        float maxSide = Mathf.Max(width, height);
        float padding = 3.25f;
        float usable = Mathf.Max(1f, size - (padding * 2f) - 1f);

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0)
                continue;

            Vector2 previous = NormalizePoint(stroke[0]);
            StampInk(ink, size, size, previous.x, previous.y, brushRadius, 1f);
            for (int i = 1; i < stroke.Count; i++)
            {
                Vector2 current = NormalizePoint(stroke[i]);
                DrawInkLine(ink, size, size, previous, current, brushRadius);
                previous = current;
            }
        }

        return ink;

        Vector2 NormalizePoint(Vector2 point)
        {
            float nx = ((point.x - bounds.minX) / maxSide) + ((maxSide - width) / maxSide) * 0.5f;
            float ny = ((point.y - bounds.minY) / maxSide) + ((maxSide - height) / maxSide) * 0.5f;
            return new Vector2(padding + nx * usable, padding + ny * usable);
        }
    }

    static void DrawInkLine(float[] ink, int width, int height, Vector2 from, Vector2 to, float radius)
    {
        float distance = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance * 1.5f));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
            StampInk(ink, width, height, point.x, point.y, radius, 1f);
        }
    }

    static void StampInk(float[] ink, int width, int height, float cx, float cy, float radius, float strength)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt(cx - radius - 1f), 0, width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(cx + radius + 1f), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(cy - radius - 1f), 0, height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(cy + radius + 1f), 0, height - 1);
        float safeRadius = Mathf.Max(0.001f, radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float coverage = Mathf.Clamp01(1f - ((distance - safeRadius + 0.75f) / 1.5f)) * strength;
                int index = y * width + x;
                if (coverage > ink[index])
                    ink[index] = coverage;
            }
        }
    }

    static Bounds2D MeasureBounds(List<List<Vector2>> strokes)
    {
        var bounds = new Bounds2D
        {
            minX = float.MaxValue,
            maxX = float.MinValue,
            minY = float.MaxValue,
            maxY = float.MinValue,
        };

        foreach (List<Vector2> stroke in strokes)
        {
            if (stroke == null)
                continue;

            foreach (Vector2 point in stroke)
            {
                if (point.x < bounds.minX) bounds.minX = point.x;
                if (point.x > bounds.maxX) bounds.maxX = point.x;
                if (point.y < bounds.minY) bounds.minY = point.y;
                if (point.y > bounds.maxY) bounds.maxY = point.y;
            }
        }

        if (bounds.minX == float.MaxValue)
            return new Bounds2D { minX = 0f, maxX = 1f, minY = 0f, maxY = 1f };

        if (Mathf.Abs(bounds.maxX - bounds.minX) < 1f)
            bounds.maxX = bounds.minX + 1f;
        if (Mathf.Abs(bounds.maxY - bounds.minY) < 1f)
            bounds.maxY = bounds.minY + 1f;
        return bounds;
    }

    static int CountPoints(List<List<Vector2>> strokes)
    {
        int count = 0;
        if (strokes == null)
            return 0;

        foreach (List<Vector2> stroke in strokes)
            if (stroke != null)
                count += stroke.Count;
        return count;
    }

    static TinyLetterNeuralRecognizer.NeuralResult EmptyResult()
    {
        return new TinyLetterNeuralRecognizer.NeuralResult
        {
            name = "Unknown",
            confidence = 0f,
            runnerUpName = "Unknown",
            runnerUpConfidence = 0f,
            isAmbiguous = false,
        };
    }

    static string ClassToUppercaseLetter(int classId)
    {
        int offset = classId - 10;
        if (offset < 0 || offset >= 26)
            return "Unknown";
        return ((char)('A' + offset)).ToString();
    }

    struct Bounds2D
    {
        public float minX;
        public float maxX;
        public float minY;
        public float maxY;
    }

    sealed class LetterCnnWeights
    {
        public float[] conv1Kernel;
        public float[] conv1Bias;
        public float[] conv2Kernel;
        public float[] conv2Bias;
        public float[] dense1Kernel;
        public float[] dense1Bias;
        public float[] dense2Kernel;
        public float[] dense2Bias;
        public float[] dense3Kernel;
        public float[] dense3Bias;

        public static LetterCnnWeights Read(byte[] bytes)
        {
            var loaded = new LetterCnnWeights();
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                byte[] magic = reader.ReadBytes(7);
                if (magic.Length != 7 ||
                    magic[0] != (byte)'T' ||
                    magic[1] != (byte)'S' ||
                    magic[2] != (byte)'C' ||
                    magic[3] != (byte)'N' ||
                    magic[4] != (byte)'N' ||
                    magic[5] != (byte)'1' ||
                    magic[6] != 0)
                    throw new InvalidDataException("Unexpected letter CNN weight header.");

                int tensorCount = reader.ReadInt32();
                for (int i = 0; i < tensorCount; i++)
                {
                    string name = ReadName(reader);
                    int rank = reader.ReadInt32();
                    int elementCount = 1;
                    for (int axis = 0; axis < rank; axis++)
                        elementCount *= reader.ReadInt32();

                    float[] values = new float[elementCount];
                    for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                        values[valueIndex] = reader.ReadSingle();

                    loaded.Assign(name, values);
                }
            }

            loaded.Validate();
            return loaded;
        }

        static string ReadName(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            byte[] bytes = reader.ReadBytes(length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        void Assign(string name, float[] values)
        {
            switch (name)
            {
                case "conv1_kernel":
                    conv1Kernel = values;
                    break;
                case "conv1_bias":
                    conv1Bias = values;
                    break;
                case "conv2_kernel":
                    conv2Kernel = values;
                    break;
                case "conv2_bias":
                    conv2Bias = values;
                    break;
                case "dense1_kernel":
                    dense1Kernel = values;
                    break;
                case "dense1_bias":
                    dense1Bias = values;
                    break;
                case "dense2_kernel":
                    dense2Kernel = values;
                    break;
                case "dense2_bias":
                    dense2Bias = values;
                    break;
                case "dense3_kernel":
                    dense3Kernel = values;
                    break;
                case "dense3_bias":
                    dense3Bias = values;
                    break;
            }
        }

        void Validate()
        {
            Require(conv1Kernel, 5 * 5 * 1 * 6, "conv1_kernel");
            Require(conv1Bias, 6, "conv1_bias");
            Require(conv2Kernel, 5 * 5 * 6 * 16, "conv2_kernel");
            Require(conv2Bias, 16, "conv2_bias");
            Require(dense1Kernel, 400 * 120, "dense1_kernel");
            Require(dense1Bias, 120, "dense1_bias");
            Require(dense2Kernel, 120 * 84, "dense2_kernel");
            Require(dense2Bias, 84, "dense2_bias");
            Require(dense3Kernel, 84 * ClassCount, "dense3_kernel");
            Require(dense3Bias, ClassCount, "dense3_bias");
        }

        static void Require(float[] values, int expectedLength, string name)
        {
            if (values == null || values.Length != expectedLength)
                throw new InvalidDataException($"{name} has unexpected shape.");
        }
    }
}
