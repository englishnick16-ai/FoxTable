using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
namespace FoxTable.Editor
{
    public static class WorldAssetPreparation
    {
        private static void Extract(string source,string destination)
        {
            var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;Directory.CreateDirectory(root);
            using(var archive=ZipFile.OpenRead(source))foreach(var entry in archive.Entries){
                if(string.IsNullOrEmpty(entry.Name))continue;
                string target=Path.GetFullPath(Path.Combine(root,entry.FullName));
                if(!target.StartsWith(root,StringComparison.Ordinal))throw new InvalidDataException("Asset archive path outside destination");
                byte[] bytes;using(var stream=entry.Open())using(var data=new MemoryStream()){stream.CopyTo(data);bytes=data.ToArray();}
                if(File.Exists(target) && Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(target)))==Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(bytes)))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,bytes);
            }
        }
        public static void Ensure()
        {
            Extract("Art/KayKit/WorldArt.zip","Assets/FoxTable/Resources/World");
            Extract("Art/KayKit/WorldModels.zip","Assets/FoxTable/WorldModels");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var path in Directory.GetFiles("Assets/FoxTable/Resources/World","*.png")){
                var importer=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;
                if(importer==null)throw new Exception("Missing texture importer: "+path);
                if(importer.textureShape==TextureImporterShape.Texture2D && !importer.mipmapEnabled && !importer.isReadable && importer.maxTextureSize==256)continue;
                importer.textureType=TextureImporterType.Default;importer.textureShape=TextureImporterShape.Texture2D;
                importer.mipmapEnabled=false;importer.isReadable=false;importer.alphaIsTransparency=true;importer.maxTextureSize=256;
                importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            }
            Debug.Log("FoxTable KayKit assets prepared: "+Directory.GetFiles("Assets/FoxTable/Resources/World","*.png").Length);
        }
    }
}
