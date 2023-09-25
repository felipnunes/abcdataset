using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using System.Numerics;

public class ModelNormalizer : MonoBehaviour
{
        string modelsPath = "C:\\Users\\felip\\Documents\\IC_Projeto\\ModelosTratados";
        string[] rawFileNames;
        string[] fileNamesPath;
        int fileNumber = 0;
    // Main Method
    public void NormilizeResourcesModels()
    {
        

            rawFileNames = System.IO.Directory.GetFiles(modelsPath);
            fileNamesPath = ObjectFilter(rawFileNames);


            //Read all files in a modesPath directory
            foreach (String fileName in fileNamesPath)
            {
                string[] lines = File.ReadAllLines(fileName);

                if (!lines[0].Contains("Normalized"))
                {
                    List<System.Numerics.Vector3> vertices = new List<System.Numerics.Vector3>();
                    int numvertices = 0;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        //Garants that the program only interact with vertices
                        if (lines[i] != "" && lines[i][0].Equals('v'))
                        {
                            string[] splitedLine = lines[i].Split(' ');

                            if (lines[i][2].Equals(' '))
                            {
                                vertices.Add(new System.Numerics.Vector3(float.Parse(splitedLine[2], CultureInfo.InvariantCulture), float.Parse(splitedLine[3], CultureInfo.InvariantCulture), float.Parse(splitedLine[4], CultureInfo.InvariantCulture)));
                                numvertices++;
                            }
                            else if (lines[i][1].Equals(' '))
                            {
                                vertices.Add(new System.Numerics.Vector3(float.Parse(splitedLine[1], CultureInfo.InvariantCulture), float.Parse(splitedLine[2], CultureInfo.InvariantCulture), float.Parse(splitedLine[3], CultureInfo.InvariantCulture)));
                                numvertices++;
                            }
                        }
                    }

                    NormalizeVertices(vertices, numvertices);
                    RewriteLines(lines, vertices, numvertices, fileName);

                    fileNumber++;
                    Debug.Log("Arquivo: " + fileName + " " + fileNumber + " Normalized");

                }    
            }

    }

    /*Rewrite .OBJ files using normalized vertices*/
    private static void RewriteLines(string[] lines, List<System.Numerics.Vector3> vertices, int numVertices, string fileName)
    {

            int actualVertice = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != "" && lines[i][0] == 'v' && actualVertice < numVertices)
                {
                    string[] splitedLine = lines[i].Split(' ');

                    if (lines[i][2].Equals(' '))
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
    static float MinValue(List<System.Numerics.Vector3> vertices)
    {

        System.Numerics.Vector3 minValuesByAxis = vertices[0];

        foreach (System.Numerics.Vector3 vertice in vertices)
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

        if (minValuesByAxis.X < minValuesByAxis.Y && minValuesByAxis.X < minValuesByAxis.Z)
        {
            return minValuesByAxis.X;
        }
        else if (minValuesByAxis.Y < minValuesByAxis.X && minValuesByAxis.Y < minValuesByAxis.Z)
        {
            return minValuesByAxis.Y;
        }

        return minValuesByAxis.Z;
    }

    /*Finds the higher values in vertices axis.*/
    static float MaxValue(List<System.Numerics.Vector3> vertices)
    {
        System.Numerics.Vector3 maxValuesByAxis = vertices[0];

        foreach (System.Numerics.Vector3 vertice in vertices)
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

        if (maxValuesByAxis.X < maxValuesByAxis.Y && maxValuesByAxis.X < maxValuesByAxis.Z)
        {
            return maxValuesByAxis.X;
        }
        else if (maxValuesByAxis.Y < maxValuesByAxis.X && maxValuesByAxis.Y < maxValuesByAxis.Z)
        {
            return maxValuesByAxis.Y;
        }

        return maxValuesByAxis.Z;
    }


    /*Normalize vertices one by one.*/
    public static List<System.Numerics.Vector3> NormalizeVertices(List<System.Numerics.Vector3> vertices, int verticesNumber)
    {
        //Finds max and min values in vertices axis
        float minValuesByAxis = MinValue(vertices);
        float maxValuesByAxis = MaxValue(vertices);
        int i = 0;
        while (i < verticesNumber)
        {
            vertices[i] = new System.Numerics.Vector3(2.0f * (vertices[i].X - minValuesByAxis) / (maxValuesByAxis - minValuesByAxis) - 1.0f,
                                                      2.0f * (vertices[i].Y - minValuesByAxis) / (maxValuesByAxis - minValuesByAxis) - 1.0f,
                                                      2.0f * (vertices[i].Z - minValuesByAxis) / (maxValuesByAxis - minValuesByAxis) - 1.0f);
            i++;

        }
        return vertices;
    }
}
