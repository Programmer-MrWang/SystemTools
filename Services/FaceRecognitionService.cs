using DlibDotNet;
using System;
using System.IO;
using System.Linq;
using DlibDotNet.Dnn;

namespace SystemTools.Services;

public class FaceRecognitionService : IDisposable
{
    private readonly string _modelDir;
    private FrontalFaceDetector? _faceDetector; 
    private ShapePredictor? _shapePredictor;
    
    private LossMetric? _faceRecognizer; 
    
    private bool _isInitialized;

    public FaceRecognitionService(string pluginFolder)
    {
        _modelDir = Path.Combine(pluginFolder, "Models");
    }

    public bool Initialize()
    {
        if (_isInitialized) return true;
        
        try
        {
            string predictorPath = Path.Combine(_modelDir, "shape_predictor_68_face_landmarks.dat");
            string recognizerPath = Path.Combine(_modelDir, "dlib_face_recognition_resnet_model_v1.dat");

            if (!File.Exists(predictorPath) || !File.Exists(recognizerPath))
                return false;

            _faceDetector = Dlib.GetFrontalFaceDetector();
            _shapePredictor = ShapePredictor.Deserialize(predictorPath);
            
            _faceRecognizer = LossMetric.Deserialize(recognizerPath);

            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dlib 初始化失败: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public bool IsModelAvailable => Directory.Exists(_modelDir) && 
        File.Exists(Path.Combine(_modelDir, "shape_predictor_68_face_landmarks.dat"));

    public float[]? ExtractFaceEncoding(byte[] rgbData, int width, int height)
    {
        if (!_isInitialized || _faceDetector == null || _shapePredictor == null || _faceRecognizer == null) 
            return null;

        using var image = Dlib.LoadImageData<RgbPixel>(rgbData, (uint)height, (uint)width, (uint)(width * 3));

        var detections = _faceDetector.Operator(image);
        if (detections == null || detections.Length == 0) return null;

        var faceRect = detections.OrderByDescending(d => d.Width * d.Height).First();
    
        using var shape = _shapePredictor.Detect(image, faceRect);
        using var chipDetail = Dlib.GetFaceChipDetails(shape, 150, 0.25);
        using var faceChip = Dlib.ExtractImageChip<RgbPixel>(image, chipDetail);
    
        using var matrix = new Matrix<RgbPixel>(faceChip);
    
        var results = _faceRecognizer.Operator(matrix);
        using var faceDescriptor = results.First(); 
    
        return faceDescriptor.ToArray();
    }

    private static float[] MatrixToArray(Matrix<float> matrix)
    {
        int rows = (int)matrix.Rows;
        int cols = (int)matrix.Columns;
        int size = rows * cols;
    
        float[] result = new float[size];
    
        int index = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                result[index++] = matrix[r, c];
            }
        }
    
        return result;
    }


    public double ComputeDistance(float[] a, float[] b)
    {
        if (a.Length != b.Length) return double.MaxValue;
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double diff = a[i] - b[i];
            sum += diff * diff;
        }
        return Math.Sqrt(sum);
    }

    public string EncodeToString(float[] encoding) => 
        Convert.ToBase64String(encoding.SelectMany(BitConverter.GetBytes).ToArray());

    public float[]? DecodeFromString(string str)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(str);
            float[] result = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _faceDetector?.Dispose();
        _shapePredictor?.Dispose();
        _faceRecognizer?.Dispose();
        _isInitialized = false;
    }
}
