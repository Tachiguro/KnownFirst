# -*- coding: utf-8 -*-
import os
import sys

try:
    from PIL import Image
except ImportError:
    print("Error: Pillow is required. Please install it using 'pip install Pillow'.")
    sys.exit(1)

def main():
    src_path = "branding/knownfirst_picture.png"

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
    width, height = img.size

    # Automatische Erkennung der Hintergrundfarbe im Motiv (bei ca. 15% Tiefe)
    sample_x = width // 2
    sample_y = int(height * 0.15)
    bg_color = img.getpixel((sample_x, sample_y))

    # Flood-fill detection from the edges to find outer white pixels
    pixels = img.load()
    visited = set()
    queue = []

    # Initialize queue with all edge pixels
    for x in range(width):
        queue.append((x, 0))
        queue.append((x, height - 1))
    for y in range(height):
        queue.append((0, y))
        queue.append((width - 1, y))

    def is_outer_bg(p):
        r, g, b, a = p
        return a == 0 or (r > 200 and g > 200 and b > 200)

    for start_node in queue:
        if start_node not in visited and is_outer_bg(pixels[start_node[0], start_node[1]]):
            visited.add(start_node)
            q = [start_node]
            while q:
                cx, cy = q.pop(0)
                # Replace with the dark blue background color
                pixels[cx, cy] = bg_color

                # Check neighbors
                for nx, ny in [(cx-1, cy), (cx+1, cy), (cx, cy-1), (cx, cy+1)]:
                    if 0 <= nx < width and 0 <= ny < height:
                        if (nx, ny) not in visited:
                            if is_outer_bg(pixels[nx, ny]):
                                visited.add((nx, ny))
                                q.append((nx, ny))

    target_size = 1024
    if img.size != (target_size, target_size):
        img = img.resize((target_size, target_size), Image.Resampling.LANCZOS)

    out_path = os.path.join(out_dir, "appicon.png")
    img.save(out_path)

    if mode == "final":
        print("Final icon generated successfully.")
    else:
        print(f"TEMP_DIR={out_dir}")

if __name__ == "__main__":
    main()
