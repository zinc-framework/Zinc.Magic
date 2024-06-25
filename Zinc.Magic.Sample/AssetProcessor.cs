using System.Collections.Generic;

namespace Zinc.Magic.Sample;

public class AssetTarget : System.Attribute
{
    //TODO: could add in file filters and stuff
    public string Extension;
    public AssetTarget(string extension)
    {
        Extension = extension;
    }
}

public abstract class AssetProcessor
{
    public abstract List<string> ProcessAsset(string path);
}

[AssetTarget(".sample")]
public class SampleProcessor : AssetProcessor
{
    public override List<string> ProcessAsset(string path)
    {
        return ["""
        // This is a sample file
        """];
    }
}