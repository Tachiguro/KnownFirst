import os
import sys
from PIL import Image
import xml.etree.ElementTree as ET

def verify():
    errors = []
    
    # 1. File exists
    icon_path = 'Resources/AppIcon/appicon_windows.png'
    if not os.path.exists(icon_path):
        errors.append(f"Missing {icon_path}")
        return errors
        
    try:
        img = Image.open(icon_path)
        # 2. Format and mode
        if img.format != 'PNG':
            errors.append(f"Icon is not PNG, it is {img.format}")
        
        img = img.convert('RGBA')
        
        w, h = img.size
        # 3. Square
        if w != h:
            errors.append(f"Icon is not square: {w}x{h}")
            
        # 4. At least 1024x1024
        if w < 1024 or h < 1024:
            errors.append(f"Icon is too small: {w}x{h}")
            
        # 5. Corners transparent
        corners = [
            img.getpixel((0, 0)),
            img.getpixel((w-1, 0)),
            img.getpixel((0, h-1)),
            img.getpixel((w-1, h-1))
        ]
        for idx, c in enumerate(corners):
            if c[3] != 0:
                errors.append(f"Corner {idx} is not transparent: {c}")
                
        # 6, 7, 8, 9. Bounding box, centering, no touch
        bbox = img.getbbox()
        if not bbox:
            errors.append("Icon is completely transparent")
        else:
            bw = bbox[2] - bbox[0]
            bh = bbox[3] - bbox[1]
            # Not touching edges
            if bbox[0] == 0 or bbox[1] == 0 or bbox[2] == w or bbox[3] == h:
                errors.append(f"Icon touches edges: {bbox}")
            # Large part of area (~80-90% means >60% and <95% dimension)
            if bw < w * 0.6 or bh < h * 0.6:
                errors.append(f"Icon takes up too little space: {bw}x{bh}")
            if bw > w * 0.95 or bh > h * 0.95:
                errors.append(f"Icon takes up too much space: {bw}x{bh}")
                
            # Roughly centered (center of bbox close to center of image)
            cx, cy = (bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2
            if abs(cx - w/2) > w * 0.05 or abs(cy - h/2) > h * 0.05:
                errors.append(f"Icon not centered. Center is {cx},{cy}, expected {w/2},{h/2}")
                
    except Exception as e:
        errors.append(f"Failed to process image: {e}")
        
    # Check KnownFirst.csproj
    try:
        with open('KnownFirst.csproj', 'r', encoding='utf-8') as f:
            csproj = f.read()
        
        # 10, 11
        if '<MauiIcon Include="Resources\\AppIcon\\appicon.png" Condition="$([MSBuild]::GetTargetPlatformIdentifier(\'$(TargetFramework)\')) != \'windows\'" />' not in csproj:
            errors.append("Android MauiIcon config is missing or altered")
            
        if '<MauiIcon Include="Resources\\AppIcon\\appicon_windows.png" Condition="$([MSBuild]::GetTargetPlatformIdentifier(\'$(TargetFramework)\')) == \'windows\'" />' not in csproj:
            errors.append("Windows MauiIcon config is missing or altered")
            
        # 13. Package IDs
        if '<ApplicationId>com.tachiguro.knownfirst</ApplicationId>' not in csproj:
            errors.append("Release ApplicationId changed")
        if '<ApplicationId>com.tachiguro.knownfirst.debug</ApplicationId>' not in csproj:
            errors.append("Debug ApplicationId changed")
        if '<ApplicationId>com.tachiguro.knownfirst.diagnostic</ApplicationId>' not in csproj:
            errors.append("Diagnostic ApplicationId changed")
            
    except Exception as e:
        errors.append(f"Failed to process csproj: {e}")
        
    return errors

if __name__ == '__main__':
    errs = verify()
    if errs:
        print("Validation failed:")
        for e in errs:
            print(f" - {e}")
        sys.exit(1)
    print("All icon verifications passed.")
