# BundleReplacer

## 说明

这是一个基于 <https://github.com/nesrak1/AssetsTools.NET> 制作的简易命令行工具，允许用户替换 Unity 游戏中资源包（AssetBundle）文件里包含的 MonoBehaviour、TextAsset、Texture2D、Sprite 资产。

### 使用方法

#### 可选的导入导出类型

- `MonoBehaviour`
- `TextAsset`
- `Texture2D`
- `Sprite`
- `VideoClip`

#### 导出
```
BundleReplacer.exe export -p <input_path> -o <output_path> -f <filter>
```

- `-p` 或 `--path=` 指定资源包文件路径。
- `-o` 或 `--output=` 指定导出文件的输出目录。
- `-f` 或 `--filter=` （可选）指定导出的类型，以 `,` 分隔。不填写时导出全部。

#### 替换
```
BundleReplacer.exe import -p <input_path> -r <replace_path> -o <output_path> -f <filter>
```

- `-p` 或 `--path=` 指定资源包文件路径。
- `-r` 或 `--replace=` 指定替换的文件目录。
- `-o` 或 `--output=` 指定替换后的资源包输出路径。
- `-f` 或 `--filter=` （可选）指定替换的类型，以 `,` 分隔。不填写时替换全部。
## Decription

This is a simple command line tool based on <https://github.com/nesrak1/AssetsTools.NET> that allows users to replace MonoBehaviour, TextAsset, Texture2D, and Sprite assets in Unity game AssetBundle files.

### Usage

#### Optional Import/Export Types

- `MonoBehaviour`
- `TextAsset`
- `Texture2D`
- `Sprite`
- `VideoClip`

#### Export
```
BundleReplacer.exe export -p <input_path> -o <output_path> -f <filter>
```

- `-p` or `--path=` specifies the path to the AssetBundle file.
- `-o` or `--output=` specifies the output directory for the exported files.
- `-f` or `--filter=` specifies the types to export, separated by `,`. If not specified, all types will be exported.

#### Import
```
BundleReplacer.exe import -p <input_path> -r <replace_path> -o <output_path> -f <filter>
```

- `-p` or `--path=` specifies the path to the AssetBundle file.
- `-r` or `--replace=` specifies the directory of files to replace.
- `-o` or `--output=` specifies the output path for the replaced AssetBundle.
- `-f` or `--filter=` specifies the types to replace, separated by `,`. If not specified, all types will be imported.

## License

This repo is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
