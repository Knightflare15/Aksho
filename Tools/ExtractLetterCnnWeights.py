import json
import os
import struct
import sys
import zipfile

sys.path.insert(0, os.path.join("TempPyDeps", "manual"))

import h5py
import numpy as np


SOURCE = os.path.join("Assets", "MLModels", "LetterCnn", "LeNet-5-EMNIST-Balanced.keras")
OUTPUT = os.path.join("Assets", "Resources", "LetterCnn", "emnist_balanced_lenet.bytes")
SOURCE_INFO = os.path.join("Assets", "Resources", "LetterCnn", "SOURCE.txt")


def print_archive_summary(archive_path):
    with zipfile.ZipFile(archive_path) as archive:
        print("Archive entries:")
        for entry in archive.infolist():
            print(f"  {entry.filename} {entry.file_size}")

        metadata = json.loads(archive.read("metadata.json"))
        config = json.loads(archive.read("config.json"))
        print("Metadata:", metadata)
        print("Model:", config["class_name"])
        print("Layers:")
        for index, layer in enumerate(config["config"]["layers"]):
            layer_config = layer["config"]
            print(
                index,
                layer["class_name"],
                layer_config.get("name"),
                "filters=",
                layer_config.get("filters"),
                "kernel=",
                layer_config.get("kernel_size"),
                "strides=",
                layer_config.get("strides"),
                "pool=",
                layer_config.get("pool_size"),
                "units=",
                layer_config.get("units"),
                "activation=",
                layer_config.get("activation"),
                "padding=",
                layer_config.get("padding"),
            )


def extract_weights_archive(archive_path, temporary_h5_path):
    with zipfile.ZipFile(archive_path) as archive:
        with open(temporary_h5_path, "wb") as output:
            output.write(archive.read("model.weights.h5"))


def collect_datasets(h5_path):
    datasets = {}
    with h5py.File(h5_path, "r") as h5:
        def visit(name, obj):
            if hasattr(obj, "shape"):
                datasets[name] = np.array(obj, dtype=np.float32)
                print(name, obj.shape, obj.dtype)

        h5.visititems(visit)
    return datasets


def find_dataset(datasets, suffix):
    matches = [key for key in datasets if key.endswith(suffix)]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one dataset ending with {suffix}, found {matches}")
    return datasets[matches[0]]


def write_tensor(writer, name, array):
    encoded_name = name.encode("utf-8")
    writer.write(struct.pack("<I", len(encoded_name)))
    writer.write(encoded_name)
    writer.write(struct.pack("<I", array.ndim))
    for size in array.shape:
        writer.write(struct.pack("<I", int(size)))
    writer.write(np.ascontiguousarray(array, dtype=np.float32).tobytes(order="C"))


def write_weights(datasets, output_path):
    tensors = [
        ("conv1_kernel", find_dataset(datasets, "/conv2d/vars/0")),
        ("conv1_bias", find_dataset(datasets, "/conv2d/vars/1")),
        ("conv2_kernel", find_dataset(datasets, "/conv2d_1/vars/0")),
        ("conv2_bias", find_dataset(datasets, "/conv2d_1/vars/1")),
        ("dense1_kernel", find_dataset(datasets, "/dense/vars/0")),
        ("dense1_bias", find_dataset(datasets, "/dense/vars/1")),
        ("dense2_kernel", find_dataset(datasets, "/dense_1/vars/0")),
        ("dense2_bias", find_dataset(datasets, "/dense_1/vars/1")),
        ("dense3_kernel", find_dataset(datasets, "/dense_2/vars/0")),
        ("dense3_bias", find_dataset(datasets, "/dense_2/vars/1")),
    ]

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "wb") as writer:
        writer.write(b"TSCNN1\0")
        writer.write(struct.pack("<I", len(tensors)))
        for name, array in tensors:
            write_tensor(writer, name, array)


def write_source_info(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as writer:
        writer.write(
            "Model: Ghostraptor/lenet-5-emnist-balanced\n"
            "Source: https://huggingface.co/Ghostraptor/lenet-5-emnist-balanced\n"
            "File: LeNet-5-EMNIST-Balanced.keras\n"
            "License: Apache-2.0\n"
            "Format in Unity: extracted float32 LeNet-5 weights from the pretrained Keras archive\n"
            "Classes: EMNIST Balanced 47 classes; Unity recognizer maps uppercase A-Z class ids only.\n"
        )


def main():
    temporary_h5_path = os.path.join("Assets", "MLModels", "LetterCnn", "letter_model.weights.tmp.h5")
    print_archive_summary(SOURCE)
    extract_weights_archive(SOURCE, temporary_h5_path)
    datasets = collect_datasets(temporary_h5_path)
    write_weights(datasets, OUTPUT)
    write_source_info(SOURCE_INFO)
    print(f"Wrote {OUTPUT}")


if __name__ == "__main__":
    main()
