import onnx
import numpy as np
import json
import argparse # Added for command line arguments

# Try to import matplotlib, but don't fail if it's not there
try:
    import matplotlib.pyplot as plt
    from PIL import Image
    MATPLOTLIB_AVAILABLE = True
except ImportError:
    MATPLOTLIB_AVAILABLE = False
    print("Matplotlib or PIL not found. Visualization functions will not be available.")

def visualize_segmentation(output, output_path="segmentation_visualization.png"):
    if not MATPLOTLIB_AVAILABLE:
        print("Cannot visualize segmentation: Matplotlib/PIL not available.")
        return

    # Assuming output is a 3D tensor: (batch_size, num_classes, height, width)
    # Or a 4D tensor from argmax: (batch_size, 1, height, width) or (batch_size, height, width)
    
    print(f"Visualizing output with shape: {output.shape}")

    if output.ndim == 4 and output.shape[1] > 1: # Raw logits/probabilities
        # Take the first batch element
        output_data = output[0]
        # Get the class with the highest probability for each pixel
        segmentation_map = np.argmax(output_data, axis=0)
    elif (output.ndim == 4 and output.shape[1] == 1): # Already argmaxed, with channel dim
        segmentation_map = output[0, 0]
    elif output.ndim == 3: # Already argmaxed, no channel dim
        segmentation_map = output[0]
    elif output.ndim == 2: # Single image, no batch, no channel
        segmentation_map = output
    else:
        print(f"Unsupported output shape for visualization: {output.shape}")
        return

    height, width = segmentation_map.shape
    print(f"Segmentation map shape: ({height}, {width})")

    # Create a color map (you can customize this)
    # Example: 21 classes for ADE20K, first few are prominent
    colors = np.array([
        [0, 0, 0], [128, 0, 0], [0, 128, 0], [128, 128, 0], [0, 0, 128],
        [128, 0, 128], [0, 128, 128], [128, 128, 128], [64, 0, 0], [192, 0, 0],
        [64, 128, 0], [192, 128, 0], [64, 0, 128], [192, 0, 128], [64, 128, 128],
        [192, 128, 128], [0, 64, 0], [128, 64, 0], [0, 192, 0], [128, 192, 0],
        [0, 64, 128]
    ] * (segmentation_map.max() // 21 + 1)) # Repeat colors if more classes than defined

    colored_image = np.zeros((height, width, 3), dtype=np.uint8)
    for r in range(height):
        for c in range(width):
            colored_image[r, c] = colors[segmentation_map[r, c] % len(colors)] # Use modulo for safety

    try:
        img = Image.fromarray(colored_image, 'RGB')
        img.save(output_path)
        print(f"Segmentation visualization saved to {output_path}")
    except Exception as e:
        print(f"Error saving visualization: {e}")

def print_model_info(model_path):
    try:
        model = onnx.load(model_path)
        print(f"Successfully loaded ONNX model from: {model_path}")
    except Exception as e:
        print(f"Error loading ONNX model: {e}")
        return

    print("\\n=== Model Information ===")
    print(f"IR Version: {model.ir_version}")
    print(f"Producer Name: {model.producer_name}")
    print(f"Producer Version: {model.producer_version}")
    print(f"Domain: {model.domain}")
    print(f"Model Version: {model.model_version}")
    if model.doc_string:
        print(f"Doc String: {model.doc_string}")

    print("\\n=== Inputs ===")
    for i, input_tensor in enumerate(model.graph.input):
        print(f"Input {i}:")
        print(f"  Name: {input_tensor.name}")
        # print(f"  Type: {input_tensor.type}") # Detailed type info
        # Iterate through dimensions of the tensor shape
        dims_str = []
        if input_tensor.type.tensor_type.HasField("shape"):
            for dim in input_tensor.type.tensor_type.shape.dim:
                if dim.HasField("dim_value"):
                    dims_str.append(str(dim.dim_value))
                elif dim.HasField("dim_param"):
                    dims_str.append(dim.dim_param) # Symbolic dimension
                else:
                    dims_str.append("?") # Unknown or dynamic
            print(f"  Shape: [{', '.join(dims_str)}]")
            print(f"  Element Type: {onnx.TensorProto.DataType.Name(input_tensor.type.tensor_type.elem_type)}")
        else:
            print("  Shape: Not defined in graph input")
            print("  Element Type: Not defined in graph input")


    print("\\n=== Outputs ===")
    for i, output_tensor in enumerate(model.graph.output):
        print(f"Output {i}:")
        print(f"  Name: {output_tensor.name}")
        # print(f"  Type: {output_tensor.type}") # Detailed type info
        dims_str = []
        if output_tensor.type.tensor_type.HasField("shape"):
            for dim in output_tensor.type.tensor_type.shape.dim:
                if dim.HasField("dim_value"):
                    dims_str.append(str(dim.dim_value))
                elif dim.HasField("dim_param"):
                    dims_str.append(dim.dim_param) # Symbolic dimension
                else:
                    dims_str.append("?")
            print(f"  Shape: [{', '.join(dims_str)}]")
            print(f"  Element Type: {onnx.TensorProto.DataType.Name(output_tensor.type.tensor_type.elem_type)}")
        else:
            print("  Shape: Not defined in graph output")
            print("  Element Type: Not defined in graph output")


    print("\\n=== Metadata (Opset Imports) ===")
    for opset_import in model.opset_import:
        print(f"  Domain: {opset_import.domain or '(default)'}, Version: {opset_import.version}")

    print("\\n=== Model Metadata Properties ===")
    if hasattr(model, 'metadata_props') and model.metadata_props:
        for prop in model.metadata_props:
            print(f"  {prop.key}: {prop.value}")
            if prop.key == "labels" or prop.key == "class_labels" or prop.key == "classes":
                try:
                    # Attempt to parse if it's a JSON string
                    labels = json.loads(prop.value)
                    print("    Parsed class labels:")
                    if isinstance(labels, list):
                        for idx, label_name in enumerate(labels):
                            print(f"      Index {idx}: {label_name}")
                    elif isinstance(labels, dict):
                        for key, value in labels.items():
                             print(f"      Key '{key}': {value}")
                    else:
                        print(f"      Labels are in an unrecognized format: {type(labels)}")
                except json.JSONDecodeError:
                    print(f"    Value for '{prop.key}' is not a valid JSON string. Raw value: {prop.value}")
                except Exception as e:
                    print(f"    Error processing labels for '{prop.key}': {e}")


    else:
        print("  No metadata properties found in the model.")

    # Example of running inference with dummy data (if needed for debugging shape issues)
    # This requires onnxruntime. If you have it, you can uncomment.
    # try:
    #     import onnxruntime
    #     sess = onnxruntime.InferenceSession(model_path, providers=['CPUExecutionProvider'])
    #     input_name = sess.get_inputs()[0].name
    #     input_shape = sess.get_inputs()[0].shape
    #     # Create dummy input data, ensure it matches the expected type (e.g., float32)
    #     # Adjust batch size and other dimensions as needed by your model
    #     dummy_input_data = np.random.randn(*[1 if isinstance(d, str) else d for d in input_shape]).astype(np.float32)

    #     print(f"\\nRunning inference with dummy input of shape: {dummy_input_data.shape} for input '{input_name}'")
    #     result = sess.run(None, {input_name: dummy_input_data})
    #     print(f"Output tensor shapes from dummy run:")
    #     for i, res_tensor in enumerate(result):
    #         print(f"  Output {i} shape: {res_tensor.shape}")
    #         # If you want to visualize the first output tensor if it's segmentation-like:
    #         if i == 0 and MATPLOTLIB_AVAILABLE: #  and res_tensor.ndim >= 2
    #             visualize_segmentation(res_tensor, output_path=f"{model_path}_dummy_output_visualization.png")

    # except ImportError:
    #     print("\\nSkipping dummy inference run: onnxruntime not found.")
    # except Exception as e:
    #     print(f"\\nError during dummy inference run: {e}")


def preprocess_image(image_path, height, width):
    if not MATPLOTLIB_AVAILABLE:
        print("Cannot preprocess image: Matplotlib/PIL not available.")
        return None
    try:
        img = Image.open(image_path).convert('RGB')
        img = img.resize((width, height), Image.BILINEAR)
        img_data = np.array(img)
        img_data = np.transpose(img_data, (2, 0, 1)) # HWC to CHW
        img_data = np.expand_dims(img_data, axis=0) # Add batch dimension
        img_data = img_data.astype(np.float32) / 255.0 # Normalize to 0-1
        return img_data
    except Exception as e:
        print(f"Error preprocessing image {image_path}: {e}")
        return None

def inspect_with_image(model_path, image_path):
    try:
        import onnxruntime
        model = onnx.load(model_path)
    except Exception as e:
        print(f"Error loading ONNX model for image inspection: {e}")
        return

    print_model_info(model_path) # Print basic info first

    if not image_path:
        print("\\nNo image provided for inference test.")
        return

    try:
        sess = onnxruntime.InferenceSession(model_path, providers=['CPUExecutionProvider'])
        input_meta = sess.get_inputs()[0]
        input_name = input_meta.name
        input_shape = input_meta.shape # e.g. ['batch_size', 3, 'height', 'width']
        
        # Determine model's expected height and width from its input shape
        # Assuming NCHW format [batch, channels, height, width]
        # Handle dynamic dimensions by using a default if needed.
        model_height = input_shape[2] if isinstance(input_shape[2], int) else 224 # Default if dynamic
        model_width = input_shape[3] if isinstance(input_shape[3], int) else 224  # Default if dynamic
        
        print(f"Model expects input shape: {input_shape}. Using HxW: {model_height}x{model_width} for preprocessing.")

        img_data = preprocess_image(image_path, model_height, model_width)
        if img_data is None:
            return

        print(f"\\nRunning inference with image: {image_path}, preprocessed to shape: {img_data.shape}")
        result = sess.run(None, {input_name: img_data})
        
        print(f"\\nOutput tensor(s) from image inference:")
        for i, res_tensor in enumerate(result):
            print(f"  Output {i} name: {sess.get_outputs()[i].name}, shape: {res_tensor.shape}, dtype: {res_tensor.dtype}")
            # You can add more details, like min/max/mean of the output tensor
            print(f"    Min: {np.min(res_tensor)}, Max: {np.max(res_tensor)}, Mean: {np.mean(res_tensor)}")

            # Attempt to visualize if it's the primary output and visualization is available
            if i == 0 and MATPLOTLIB_AVAILABLE: #  and res_tensor.ndim >=2
                 visualize_segmentation(res_tensor, output_path=f"{model_path.split('/')[-1]}_{image_path.split('/')[-1]}_visualization.png")

    except ImportError:
        print("\\nSkipping inference with image: onnxruntime not found.")
    except Exception as e:
        print(f"\\nError during inference with image: {e}")


def main():
    parser = argparse.ArgumentParser(description="Inspect an ONNX model and optionally run inference on an image.")
    parser.add_argument("model_path", type=str, help="Path to the .onnx or .sentis model file.")
    parser.add_argument("--image", type=str, help="Optional path to an image for inference test.")
    
    args = parser.parse_args()

    if args.image:
        inspect_with_image(args.model_path, args.image)
    else:
        print_model_info(args.model_path)

if __name__ == "__main__":
    main() 