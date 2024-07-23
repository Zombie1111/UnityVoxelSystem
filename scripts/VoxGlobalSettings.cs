using UnityEngine;

namespace zombVoxels
{
    public static class VoxGlobalSettings
    {
        //The size of each voxel in worldSpace (Extents)
        public const float voxelSizeWorld = 0.5f;

        //How many voxels there are in the scene in total
        public const int voxelAxisCount = 200;

        public static readonly Color[] diffColors64 = new Color[64]
        {
            new Color(0.412f, 0.412f, 0.412f),  // dimgray (#696969)
            new Color(0.663f, 0.663f, 0.663f),  // darkgray (#a9a9a9)
            new Color(0.863f, 0.863f, 0.863f),  // gainsboro (#dcdcdc)
            new Color(0.184f, 0.184f, 0.184f),  // darkslategray (#2f4f4f)
            new Color(0.333f, 0.42f, 0.184f),   // darkolivegreen (#556b2f)
            new Color(0.412f, 0.545f, 0.137f),  // olivedrab (#6b8e23)
            new Color(0.627f, 0.322f, 0.176f),  // sienna (#a0522d)
            new Color(0.647f, 0.322f, 0.176f),  // brown (#a52a2a)
            new Color(0.18f, 0.545f, 0.341f),   // seagreen (#2e8b57)
            new Color(0.098f, 0.098f, 0.439f),  // midnightblue (#191970)
            new Color(0.282f, 0.239f, 0.545f),  // darkslateblue (#483d8b)
            new Color(0.373f, 0.62f, 0.627f),   // cadetblue (#5f9ea0)
            new Color(0f, 0.502f, 0f),          // green (#008000)
            new Color(0.737f, 0.561f, 0.561f),  // rosybrown (#bc8f8f)
            new Color(0.4f, 0.2f, 0.6f),        // rebeccapurple (#663399)
            new Color(0.722f, 0.525f, 0.043f),  // darkgoldenrod (#b8860b)
            new Color(0.827f, 0.706f, 0.549f),  // darkkhaki (#bdb76b)
            new Color(0.275f, 0.51f, 0.71f),    // steelblue (#4682b4)
            new Color(0f, 0f, 0.502f),          // navy (#000080)
            new Color(0.827f, 0.161f, 0.161f),  // chocolate (#d2691e)
            new Color(0.604f, 0.804f, 0.196f),  // yellowgreen (#9acd32)
            new Color(0.863f, 0.439f, 0.576f),  // indianred (#cd5c5c)
            new Color(0.196f, 0.804f, 0.196f),  // limegreen (#32cd32)
            new Color(0.561f, 0.737f, 0.561f),  // darkseagreen (#8fbc8f)
            new Color(0.502f, 0f, 0.502f),      // purple (#800080)
            new Color(0.502f, 0f, 0f),          // maroon (#800000)
            new Color(0.4f, 0.8f, 0.667f),      // mediumaquamarine (#66cdaa)
            new Color(1f, 0f, 0f),              // red (#ff0000)
            new Color(0f, 0.78f, 0.82f),        // darkturquoise (#00ced1)
            new Color(1f, 0.647f, 0f),          // orange (#ffa500)
            new Color(1f, 0.843f, 0f),          // gold (#ffd700)
            new Color(0.78f, 0.157f, 0.576f),   // mediumvioletred (#c71585)
            new Color(0f, 0f, 0.804f),          // mediumblue (#0000cd)
            new Color(0.486f, 0.988f, 0f),      // lawngreen (#7cfc00)
            new Color(0.867f, 0.722f, 0.529f),  // burlywood (#deb887)
            new Color(0f, 1f, 0f),              // lime (#00ff00)
            new Color(0.584f, 0f, 0.827f),      // darkviolet (#9400d3)
            new Color(0.831f, 0.439f, 0.831f),  // mediumorchid (#ba55d3)
            new Color(0f, 1f, 0.498f),          // springgreen (#00ff7f)
            new Color(0.255f, 0.412f, 0.882f),  // royalblue (#4169e1)
            new Color(0.863f, 0.588f, 0.439f),  // darksalmon (#e9967a)
            new Color(0.863f, 0.078f, 0.235f),  // crimson (#dc143c)
            new Color(0f, 1f, 1f),              // aqua (#00ffff)
            new Color(0f, 0.749f, 1f),          // deepskyblue (#00bfff)
            new Color(0.957f, 0.643f, 0.376f),  // sandybrown (#f4a460)
            new Color(0.576f, 0.439f, 0.859f),  // mediumpurple (#9370db)
            new Color(0f, 0f, 1f),              // blue (#0000ff)
            new Color(1f, 0.388f, 0.278f),      // tomato (#ff6347)
            new Color(0.847f, 0.749f, 0.847f),  // thistle (#d8bfd8)
            new Color(1f, 0f, 1f),              // fuchsia (#ff00ff)
            new Color(0.859f, 0.439f, 0.576f),  // palevioletred (#db7093)
            new Color(0.941f, 0.902f, 0.549f),  // khaki (#f0e68c)
            new Color(1f, 1f, 0.333f),          // laserlemon (#ffff54)
            new Color(0.392f, 0.584f, 0.929f),  // cornflower (#6495ed)
            new Color(0.859f, 0.439f, 0.576f),  // plum (#dda0dd)
            new Color(0.565f, 0.933f, 0.565f),  // lightgreen (#90ee90)
            new Color(0.529f, 0.808f, 0.922f),  // skyblue (#87ceeb)
            new Color(1f, 0.078f, 0.576f),      // deeppink (#ff1493)
            new Color(0.686f, 0.933f, 0.933f),  // paleturquoise (#afeeee)
            new Color(0.573f, 0.302f, 0.596f),  // violet (#8e82ee)
            new Color(0.498f, 1f, 0.831f),      // aquamarine (#7fffd4)
            new Color(1f, 0.412f, 0.706f),      // hotpink (#ff69b4)
            new Color(1f, 0.894f, 0.769f),      // bisque (#ffe4c4)
            new Color(1f, 0.714f, 0.757f)       // lightpink (#ffb6c1)
        };
    }
}


