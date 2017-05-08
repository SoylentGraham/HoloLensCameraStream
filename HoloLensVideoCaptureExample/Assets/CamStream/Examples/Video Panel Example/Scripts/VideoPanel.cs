//  
// Copyright (c) 2017 Vulcan, Inc. All rights reserved.  
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.
//

using UnityEngine;
using UnityEngine.UI;

public class VideoPanel : MonoBehaviour
{
    public RawImage rawImage;

	Texture2D	ImageTexture;
	
    public void SetBytes(byte[] Image,TextureFormat Format,int Width,int Height)
    {
		if ( ImageTexture == null )
		{
			ImageTexture = new Texture2D( Width, Height, Format, false );
			rawImage.texture = ImageTexture;
		}
				
		//TODO: Should be able to do this: texture.LoadRawTextureData(pointerToImage, 1280 * 720 * 4);
        ImageTexture.LoadRawTextureData(Image); 
        ImageTexture.Apply();
    }
}
