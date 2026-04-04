import os
import requests
import zipfile
import shutil

STOCKFISH_URL = "https://github.com/official-stockfish/Stockfish/releases/download/sf_18/stockfish-windows-x86-64-avx2.zip"
DOWNLOAD_FILE = "stockfish.zip"
TARGET_DIR = "stockfish18"

def download_and_extract():
    if os.path.exists(TARGET_DIR):
        print("Stockfish already exists. Skipping download.")
        return

    print("Downloading Stockfish 16.1...")
    response = requests.get(STOCKFISH_URL, stream=True)
    with open(DOWNLOAD_FILE, "wb") as f:
        for chunk in response.iter_content(chunk_size=8192):
            f.write(chunk)
            
    print("Extracting...")
    with zipfile.ZipFile(DOWNLOAD_FILE, "r") as zip_ref:
        zip_ref.extractall("temp_stockfish")
        
    # Find the extracted folder inside temp_stockfish
    extracted_folder = [f for f in os.listdir("temp_stockfish") if os.path.isdir(os.path.join("temp_stockfish", f))][0]
    
    # Move the extracted folder to TARGET_DIR
    shutil.move(os.path.join("temp_stockfish", extracted_folder), TARGET_DIR)
    
    # Clean up
    os.remove(DOWNLOAD_FILE)
    shutil.rmtree("temp_stockfish")
    
    print("Stockfish downloaded and installed successfully!")

if __name__ == "__main__":
    download_and_extract()
