import zipfile
import os
import shutil

def create_payload_zip(zip_name, source_dir):
    # Ensure the LichessBotGUI has been built recently
    print("Assuming LichessBotGUI has been built in Release mode...")
    
    with zipfile.ZipFile(zip_name, 'w', zipfile.ZIP_DEFLATED) as zipf:
        # 1. Add python files and requirements (root-level only, skip dev/env dirs)
        skip_dirs = {'venv', '__pycache__', 'bin', 'obj', '.git', 'LichessBotGUI', 'LichessBotSetup', 'dist', 'dist_single', 'stockfish', 'stockfish18'}
        skip_files = {'settings.json', '.env'}
        for root, dirs, files in os.walk(source_dir):
            # Prune dirs in-place so os.walk won't descend into them
            dirs[:] = [d for d in dirs if d not in skip_dirs and not d.startswith('.')]
            rel_root = os.path.relpath(root, source_dir)
            # Only include root-level python/resource files
            if rel_root != '.':
                continue
            for file in files:
                if file in skip_files:
                    continue
                if file.endswith('.py') or file == 'requirements.txt':
                    filepath = os.path.join(root, file)
                    zipf.write(filepath, file)
                    
        # 2. Add Stockfish
        sf_path = os.path.join(source_dir, 'stockfish', 'stockfish-windows-x86-64-avx2.exe')
        if os.path.exists(sf_path):
            zipf.write(sf_path, r'stockfish\stockfish-windows-x86-64-avx2.exe')
            
        # 3. Add compiled GUI
        gui_dir = os.path.join(source_dir, 'LichessBotGUI', 'bin', 'Release', 'net9.0-windows')
        if os.path.exists(gui_dir):
            for root, dirs, files in os.walk(gui_dir):
                for file in files:
                    filepath = os.path.join(root, file)
                    arcname = os.path.join('LichessBotGUI', os.path.relpath(filepath, gui_dir))
                    zipf.write(filepath, arcname)

if __name__ == '__main__':
    print("Creating Payload.zip...")
    create_payload_zip('Payload.zip', '.')
    print("Done!")
