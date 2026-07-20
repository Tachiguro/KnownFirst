# -*- coding: utf-8 -*-
import os
import sys
import hashlib

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("Error: Pillow is required. Please install it using 'pip install Pillow'.")
    sys.exit(1)

def get_bounding_box(img):
    pixels = img.load()
    width, height = img.size
    min_x, min_y = width, height
    max_x, max_y = 0, 0
    
    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            # Detect anything that is not nearly white and transparent
            if a > 0 and (r < 250 or g < 250 or b < 250):
                if x < min_x: min_x = x
                if x > max_x: max_x = x
                if y < min_y: min_y = y
                if y > max_y: max_y = y
                
    return (min_x, min_y, max_x + 1, max_y + 1)

def draw_pixel_text(draw, text, start_x, start_y, pixel_size, color):
    font_data = {
        'D': [
            "110",
            "101",
            "101",
            "101",
            "110"
        ],
        'B': [
            "110",
            "101",
            "110",
            "101",
            "110"
        ],
        'G': [
            "011",
            "100",
            "101",
            "101",
            "011"
        ],
        'I': [
            "010",
            "010",
            "010",
            "010",
            "010"
        ],
        'A': [
            "010",
            "101",
            "111",
            "101",
            "101"
        ]
    }
    
    current_x = start_x
    for char in text:
        if char in font_data:
            pattern = font_data[char]
            for row_idx, row in enumerate(pattern):
                for col_idx, val in enumerate(row):
                    if val == '1':
                        px = current_x + col_idx * pixel_size
                        py = start_y + row_idx * pixel_size
                        draw.rectangle([px, py, px + pixel_size - 1, py + pixel_size - 1], fill=color)
        current_x += 4 * pixel_size # 3 cols + 1 space

def add_badge(base_img, text, bg_color_badge):
    draw = ImageDraw.Draw(base_img)
    badge_w = 260
    badge_h = 100
    margin = 150
    target_size = base_img.width
    x1 = target_size - margin - badge_w
    y1 = target_size - margin - badge_h
    x2 = target_size - margin
    y2 = target_size - margin
    
    draw.rounded_rectangle([x1, y1, x2, y2], radius=20, fill=bg_color_badge)
    
    pixel_size = 12
    text_w = 11 * pixel_size # 3 chars * 3 cols + 2 spaces = 11 cols
    text_h = 5 * pixel_size
    text_x = x1 + (badge_w - text_w) // 2
    text_y = y1 + (badge_h - text_h) // 2
    
    draw_pixel_text(draw, text, text_x, text_y, pixel_size, (255, 255, 255, 255))
    return base_img

def main():
    # Wir nehmen an, das Skript wird aus dem Repo-Root ausgefuehrt
    src_path = "branding/knownfirst_picture.png"
    
    # Prüfe ob temp oder final mode
    mode = "final"
    out_dir = "Resources/AppIcon"
    if len(sys.argv) > 1 and sys.argv[1] == "--temp":
        mode = "temp"
        import tempfile
        out_dir = tempfile.mkdtemp(prefix="knownfirst_icons_")
        print(f"Generating temporary icons in {out_dir}")
    
    if not os.path.exists(src_path):
        print(f"Error: Source file {src_path} not found.")
        sys.exit(1)
        
    if mode == "final" and not os.path.exists(out_dir):
        os.makedirs(out_dir)
        
    img = Image.open(src_path).convert("RGBA")
    bbox = get_bounding_box(img)
    motif = img.crop(bbox)
    
    # Hintergrundfarbe automatisch bestimmen (obere Mitte leicht nach innen)
    sample_x = motif.width // 2
    sample_y = int(motif.height * 0.05)
    bg_color = motif.getpixel((sample_x, sample_y))
    
    target_size = 1024
    # Sicherheitsabstand von 5% je Seite -> Motiv wird 90% so groß wie 1024
    safe_size = int(target_size * 0.90)
    
    ratio = min(safe_size / motif.width, safe_size / motif.height)
    new_w = int(motif.width * ratio)
    new_h = int(motif.height * ratio)
    
    # LANCZOS für qualitativ hochwertige deterministische Skalierung
    motif_resized = motif.resize((new_w, new_h), Image.Resampling.LANCZOS)
    
    def create_base():
        base = Image.new("RGBA", (target_size, target_size), bg_color)
        offset_x = (target_size - new_w) // 2
        offset_y = (target_size - new_h) // 2
        base.paste(motif_resized, (offset_x, offset_y), motif_resized)
        return base
        
    release = create_base()
    release_path = os.path.join(out_dir, "appicon_release.png")
    release.save(release_path)
    
    debug = create_base()
    debug = add_badge(debug, "DBG", (255, 140, 0, 255))
    debug_path = os.path.join(out_dir, "appicon_debug.png")
    debug.save(debug_path)
    
    diagnostic = create_base()
    diagnostic = add_badge(diagnostic, "DIA", (0, 150, 200, 255))
    diagnostic_path = os.path.join(out_dir, "appicon_diagnostic.png")
    diagnostic.save(diagnostic_path)
    
    if mode == "final":
        print("Final icons generated successfully.")
    else:
        print(f"TEMP_DIR={out_dir}")

if __name__ == "__main__":
    main()
