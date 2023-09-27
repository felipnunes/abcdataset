using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
// namespace declaration
namespace ModelNormalizerProgram
{
    // Class declaration
    public class ModelNormalizer
    {
       
        // Main Method
        static void Main(string[] args)
        {
            
            string modelsPath = "C:\\Users\\felip\\Desktop\\modelos";
            string[] rawFileNames;
            string[] fileNamesPath;
            rawFileNames = System.IO.Directory.GetFiles(modelsPath);
            fileNamesPath = rawFileNames;
            //fileNamesPath = ObjectFilter(rawFileNames); // filter meta files and adds to a new var.
            int fileNumber = 0;

            //Read all files in a modesPath directory
            foreach (String fileName in fileNamesPath)
            {
                string[] lines = File.ReadAllLines(fileName);
                List<Vector3> vertices = new List<Vector3>();
                int numvertices = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    // Remove espaços em branco no início e no final da linha
                    string trimmedLine = lines[i].Trim();

                    // Garante que o programa só interaja com linhas que começam com "v"
                    if (trimmedLine.StartsWith("v "))
                    {
                        string[] splitedLine = trimmedLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        if (splitedLine.Length >= 4)
                        {
                            float x = float.Parse(splitedLine[1], CultureInfo.InvariantCulture);
                            float y = float.Parse(splitedLine[2], CultureInfo.InvariantCulture);
                            float z = float.Parse(splitedLine[3], CultureInfo.InvariantCulture);

                            vertices.Add(new Vector3(x, y, z));
                            numvertices++;
                        }
                    }
                }


                NormalizeVertices(vertices, numvertices);
                RewriteLines(lines, vertices, numvertices, fileName);
                
                fileNumber++;
                Console.WriteLine("Arquivo: " + fileName + " " + fileNumber + " Normalized");
            }

            Console.ReadKey();
        }

        /*Rewrite .OBJ files using normalized vertices*/
        private static void RewriteLines(string[] lines, List<Vector3> vertices, int numVertices, string fileName)
        {
            int actualVertice = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != "" && lines[i][0] == 'v' && actualVertice < numVertices)
                {
                    string[] splitedLine = lines[i].Split(" ");
                    if (lines[i][2].Equals(' ') && lines[i][1].Equals(' '))
                    {
                        splitedLine[2] = vertices[actualVertice].X.ToString().Replace(',', '.');
                        splitedLine[3] = vertices[actualVertice].Y.ToString().Replace(',', '.');
                        splitedLine[4] = vertices[actualVertice].Z.ToString().Replace(',', '.');
                        lines[i] = "v " + splitedLine[2] + " " + splitedLine[3] + " " + splitedLine[4];
                    }
                    else if (lines[i][1].Equals(' '))
                    {
                        splitedLine[1] = vertices[actualVertice].X.ToString().Replace(',', '.');
                        splitedLine[2] = vertices[actualVertice].Y.ToString().Replace(',', '.');
                        splitedLine[3] = vertices[actualVertice].Z.ToString().Replace(',', '.');

                        lines[i] = "v " + splitedLine[1] + " " + splitedLine[2] + " " + splitedLine[3];
                    }

                    actualVertice++;
                }
            }

            string teste = "C:/Users/felip/Documents/teste.txt";

            System.IO.File.WriteAllText(fileName, string.Empty);

            using (StreamWriter sw = new StreamWriter(fileName, true))
            {
                sw.Write("# Normalized\n");
                foreach (string line in lines)
                {
                    sw.Write(line + "\n");
                }
                
            }
              
        }

        /*Ignore .meta files in directory*/
        static string[] ObjectFilter(string[] fileNames)
        {
            string[] filteredFileNames = new string[fileNames.Length / 2];

            for (int i = 0, j = 0; i < fileNames.Length; i++)
            {
                if (i % 2 == 0)
                {
                  
                    filteredFileNames[j] = fileNames[i];
                    j++;
                }
            }

            return filteredFileNames;
        }

        /*Finds the lower values in vertives axis.*/
        static Vector3 MinValue(List<Vector3> vertices)
        {

            Vector3 minValuesByAxis = vertices[0];

            foreach (Vector3 vertice in vertices)
            {
                if (vertice.X < minValuesByAxis.X)
                {
                    minValuesByAxis.X = vertice.X;
                }
                if (vertice.Y < minValuesByAxis.Y)
                {
                    minValuesByAxis.Y = vertice.Y;
                }
                if (vertice.Z < minValuesByAxis.Z)
                { 
                    minValuesByAxis.Z = vertice.Z;
                }
            }


            return minValuesByAxis;
        }

        /*Finds the higher values in vertices axis.*/
        static Vector3 MaxValue(List<Vector3> vertices)
        {
            Vector3 maxValuesByAxis = vertices[0];

            foreach (Vector3 vertice in vertices)
            {
                if (vertice.X > maxValuesByAxis.X)
                {
                    maxValuesByAxis.X = vertice.X;
                }
                if (vertice.Y > maxValuesByAxis.Y)
                {
                    maxValuesByAxis.Y = vertice.Y;
                }
                if (vertice.Z > maxValuesByAxis.Z)
                {
                    maxValuesByAxis.Z = vertice.Z;
                }
            }

            return maxValuesByAxis;
        }


        /*Normalize vertices one by one.*/
        public static List<Vector3> NormalizeVertices(List <Vector3> vertices, int verticesNumber)
        {
            //Finds max and min values in vertices axis
            Vector3 minValuesByAxis = MinValue(vertices);
            Vector3 maxValuesByAxis = MaxValue(vertices);

            Console.WriteLine("Min vector = " + minValuesByAxis.X + " " + minValuesByAxis.Y + " " + minValuesByAxis.Z);
            Console.WriteLine("Max vector = " + maxValuesByAxis.X + " " + maxValuesByAxis.Y + " " + maxValuesByAxis.Z);

            float minValue = GetOfAxisToUse(minValuesByAxis, maxValuesByAxis, "min");
            float maxValue = GetOfAxisToUse(minValuesByAxis, maxValuesByAxis, "max");

            Console.WriteLine("Min value = " + minValue + "Max value = " + maxValue);


            int i = 0;
            while (i < verticesNumber)
            {
                vertices[i] = new Vector3(2.0f * (vertices[i].X - minValue) / (maxValue - minValue) - 1.0f,
                            2.0f * (vertices[i].Y - minValue) / (maxValue - minValue) - 1.0f,
                            2.0f * (vertices[i].Z - minValue) / (maxValue - minValue) - 1.0f);
                i++;
            
            }
            return vertices;
        }

        public static float GetOfAxisToUse(Vector3 min, Vector3 max, string typeToReturn)
        {
            char axisToBeUsed;

            if(MathF.Abs(max.X - min.X)  > MathF.Abs(max.Y - min.Y) && MathF.Abs(max.X - min.X) > MathF.Abs(max.Z - min.Z))
            {
                axisToBeUsed = 'X';
            }
            else if (MathF.Abs(max.Y - min.Y) > MathF.Abs(max.X - min.X) && MathF.Abs(max.Y - min.Y) > MathF.Abs(max.Z - min.Z))
            {
                axisToBeUsed = 'Y';
            }
            else
            {
                axisToBeUsed = 'Z';
            }

            Console.WriteLine("Axis to be used = " + axisToBeUsed);

            if (axisToBeUsed == 'X' && typeToReturn.Equals("max"))
            {
                return max.X;
            }
            else if (axisToBeUsed == 'Y' && typeToReturn.Equals("max"))
            {
                return max.Y;
            }
            else if (axisToBeUsed == 'Z' && typeToReturn.Equals("max"))
            {
                return max.Z;
            }
            else if (axisToBeUsed == 'X' && typeToReturn.Equals("min"))
            {
                return min.X;
            }
            else if (axisToBeUsed == 'Y' && typeToReturn.Equals("min"))
            {
                return min.Y;
            }
            else if (axisToBeUsed == 'Z' && typeToReturn.Equals("min"))
            {
                return min.Z;
            }

            return 0;
        }
    }
}