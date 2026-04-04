import zipfile
import os

def create_source_zip(zip_name, source_dir):
    with zipfile.ZipFile(zip_name, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for root, dirs, files in os.walk(source_dir):
            # Ignore hidden dirs or build output
            dirs[:] = [d for d in dirs if not d.startswith('.') and d not in ('bin', 'obj', 'stockfish', '__pycache__')]
            
            for file in files:
                # Don't pack the .env file with secrets, or the giant stockfish executables, or the zip itself
                if file.endswith('.exe') or file.endswith('.zip') or file == '.env':
                    continue
                    
                filepath = os.path.join(root, file)
                arcname = os.path.relpath(filepath, source_dir)
                zipf.write(filepath, arcname)

if __name__ == '__main__':
    print("Creating archive LichessBot_Source.zip...")
    create_source_zip('LichessBot_Source.zip', '.')
    print("Done!")
