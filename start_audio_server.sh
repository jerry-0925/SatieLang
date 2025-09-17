#!/bin/bash

# SatieLang AudioLDM2 Server Startup Script

echo "========================================="
echo "  SatieLang AudioLDM2 Server"
echo "========================================="
echo ""

# Check if virtual environment exists
if [ ! -d "audioldm2_venv" ]; then
    echo "❌ Virtual environment not found!"
    echo "Creating virtual environment..."
    python3 -m venv audioldm2_venv

    echo "Installing dependencies..."
    source audioldm2_venv/bin/activate
    pip install --upgrade pip
    pip install -r requirements.txt
else
    echo "✅ Virtual environment found"
fi

# Activate virtual environment
echo "Activating virtual environment..."
source audioldm2_venv/bin/activate

# Check if dependencies are installed
echo "Checking dependencies..."
if ! python -c "import flask" 2>/dev/null; then
    echo "Installing missing dependencies..."
    pip install -r requirements.txt
fi

# Display GPU status
echo ""
echo "Checking hardware acceleration..."
python -c "
import torch
if torch.cuda.is_available():
    print('🚀 CUDA GPU Available!')
elif torch.backends.mps.is_available():
    print('🚀 Apple Silicon MPS GPU Available!')
else:
    print('💻 Using CPU (slower generation)')
"

# Start the server
echo ""
echo "========================================="
echo "Starting AudioLDM2 server on port 5001..."
echo "========================================="
echo ""
echo "📌 Leave this terminal open while using audio generation in Unity"
echo "📌 First generation will download the model (~3.5GB)"
echo "📌 Press Ctrl+C to stop the server"
echo ""

# Run the server
export PORT=5001
python audioldm2_server.py