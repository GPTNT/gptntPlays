from flask import Flask, request, jsonify
from flask_cors import CORS
import subprocess
import requests

HTTP_PORT = 8084
KTANE_PORT = 8085

app = Flask(__name__)
CORS(app)

def get_screen_dimensions():
    result = subprocess.run(["xdotool", "getdisplaygeometry"], capture_output=True, text=True)
    width, height = map(int, result.stdout.strip().split())
    return width, height

@app.route('/click', methods=['GET'])
def click():
    x_pos = request.args.get('x_pos')
    y_pos = request.args.get('y_pos')

    if x_pos and y_pos:
        screen_width, screen_height = get_screen_dimensions()
        x_abs = str(int(float(x_pos) * screen_width))
        y_abs = str(int(float(y_pos) * screen_height))

        subprocess.run(["xdotool", "mousemove", x_abs, y_abs, "click", "1"])   
        return jsonify({"status": "clicked", "x_pos": x_abs, "y_pos": y_abs})
    else:
        return jsonify({"error": "missing x or y parameters"}), 400

@app.route('/<path:path>', methods=['GET'])
def forward(path):
    url = f"http://localhost:{KTANE_PORT}/{path}"
    try:
        response = requests.get(url, params=request.args)
        response.raise_for_status() # raise HTTPError for bad responses (4xx or 5xx)
        return response.content, response.status_code, response.headers.items()
    except requests.exceptions.RequestException as e:
        return jsonify({"error": str(e)}), 500

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=HTTP_PORT)