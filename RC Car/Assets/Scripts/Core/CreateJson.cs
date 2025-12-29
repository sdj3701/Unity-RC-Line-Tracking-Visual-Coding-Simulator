using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreateJson : MonoBehaviour
{
    public void GenerateJson()
    {
        var exporter = FindObjectOfType<BE2_CodeExporter>();
        if (exporter == null)
        {
            var go = new GameObject("BE2_CodeExporter_Temp");
            exporter = go.AddComponent<BE2_CodeExporter>();
            
            string xml = exporter.GenerateXmlFromAllEnvs();
            BE2XmlToRuntimeJson.Export(xml);
            
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        else
        {
            string xml = exporter.GenerateXmlFromAllEnvs();
            BE2XmlToRuntimeJson.Export(xml);
        }
        
        Debug.Log("[CreateJson] JSON generation triggered manually.");
    }
}
