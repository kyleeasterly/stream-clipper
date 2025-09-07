#!/usr/bin/env python3
import json
import sys
from typing import Any, Dict, Set

def get_type_name(obj: Any) -> str:
    """Get a readable type name for an object."""
    if obj is None:
        return "null"
    elif isinstance(obj, bool):
        return "boolean"
    elif isinstance(obj, int):
        return "integer"
    elif isinstance(obj, float):
        return "number"
    elif isinstance(obj, str):
        return "string"
    elif isinstance(obj, list):
        return "array"
    elif isinstance(obj, dict):
        return "object"
    else:
        return type(obj).__name__

def analyze_structure(obj: Any, path: str = "root", depth: int = 0, max_depth: int = 5, seen_paths: Set[str] = None) -> Dict:
    """Recursively analyze JSON structure."""
    if seen_paths is None:
        seen_paths = set()
    
    if depth > max_depth:
        return {"type": "...", "note": "max depth reached"}
    
    result = {"type": get_type_name(obj)}
    
    if isinstance(obj, list):
        if len(obj) > 0:
            # Sample first few items to detect uniformity
            sample_size = min(3, len(obj))
            samples = []
            for i in range(sample_size):
                item_analysis = analyze_structure(obj[i], f"{path}[{i}]", depth + 1, max_depth, seen_paths)
                samples.append(item_analysis)
            
            # Check if all samples have the same structure
            if all(s == samples[0] for s in samples):
                result["length"] = len(obj)
                result["items"] = samples[0]
            else:
                result["length"] = len(obj)
                result["items"] = samples
        else:
            result["length"] = 0
            
    elif isinstance(obj, dict):
        result["properties"] = {}
        for key, value in obj.items():
            child_path = f"{path}.{key}"
            if child_path not in seen_paths:
                seen_paths.add(child_path)
                result["properties"][key] = analyze_structure(value, child_path, depth + 1, max_depth, seen_paths)
    
    elif isinstance(obj, str):
        # Sample string value (truncated)
        if len(obj) > 50:
            result["sample"] = obj[:47] + "..."
        else:
            result["sample"] = obj
    
    return result

def print_structure(structure: Dict, indent: int = 0):
    """Pretty print the structure analysis."""
    ind = "  " * indent
    
    if structure["type"] == "object":
        print(f"{ind}Object {{")
        if "properties" in structure:
            for key, value in structure["properties"].items():
                print(f"{ind}  {key}:", end="")
                if value["type"] in ["object", "array"]:
                    print()
                    print_structure(value, indent + 2)
                else:
                    print(f" {value['type']}", end="")
                    if "sample" in value:
                        print(f" (e.g., \"{value['sample']}\")", end="")
                    print()
        print(f"{ind}}}")
        
    elif structure["type"] == "array":
        length = structure.get("length", 0)
        print(f"{ind}Array[{length}] {{")
        if "items" in structure:
            if isinstance(structure["items"], list):
                for i, item in enumerate(structure["items"]):
                    print(f"{ind}  [{i}]:")
                    print_structure(item, indent + 2)
            else:
                print(f"{ind}  items:")
                print_structure(structure["items"], indent + 2)
        print(f"{ind}}}")
        
    else:
        print(f"{ind}{structure['type']}", end="")
        if "sample" in structure:
            print(f" (e.g., \"{structure['sample']}\")", end="")

def main():
    json_file = "data/2025-09-04_22-03-01.json"
    
    print(f"Analyzing JSON structure of: {json_file}")
    print("=" * 60)
    
    try:
        with open(json_file, 'r') as f:
            data = json.load(f)
        
        # Get basic stats
        print(f"Root type: {get_type_name(data)}")
        
        if isinstance(data, list):
            print(f"Total items: {len(data)}")
        elif isinstance(data, dict):
            print(f"Total top-level keys: {len(data.keys())}")
        
        print("\nStructure Analysis:")
        print("-" * 40)
        
        structure = analyze_structure(data)
        print_structure(structure)
        
        # If it's an array, show more detailed info about first item
        if isinstance(data, list) and len(data) > 0:
            print("\n" + "=" * 60)
            print("Detailed view of first item:")
            print("-" * 40)
            first_item_structure = analyze_structure(data[0], max_depth=10)
            print_structure(first_item_structure)
        
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON format - {e}")
    except FileNotFoundError:
        print(f"Error: File '{json_file}' not found")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    main()