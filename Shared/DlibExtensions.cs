using DlibDotNet;

namespace SystemTools.Services;

public static class DlibExtensions
{
    public static float[] ToArray(this Matrix<float> matrix)
    {
        int rows = matrix.Rows;
        int cols = matrix.Columns;
        float[] result = new float[rows * cols];
        
        // 使用循环读取 Matrix 元素
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                result[i * cols + j] = matrix[i, j];
            }
        }
        return result;
    }
}