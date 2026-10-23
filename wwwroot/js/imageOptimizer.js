/**
 * BookMyFun - Client-Side Image & Asset Optimization Pipeline
 * Automatically downscales oversized photos and converts raster images to modern WebP format
 * with high visual fidelity, dramatically reducing client upload bandwidth and server storage footprint.
 */
(function (window) {
    'use strict';

    const ImageOptimizer = {
        /**
         * Checks if a file is an optimizable raster image (JPEG, PNG, BMP, WEBP).
         * Excludes SVG and animated GIF to preserve vector paths and animation frames.
         */
        isOptimizable: function (file) {
            if (!file || !file.name) return false;
            const mime = (file.type || '').toLowerCase();
            const name = file.name.toLowerCase();

            if (mime === 'image/svg+xml' || name.endsWith('.svg') || mime === 'image/gif' || name.endsWith('.gif')) {
                return false;
            }

            return mime.startsWith('image/') ||
                /\.(jpe?g|png|webp|bmp|avif)$/i.test(name);
        },

        /**
         * Compresses and converts an image file into modern WebP format with dimension bounding.
         * @param {File} file - Original file from input
         * @param {Object} [options] - Custom constraints
         * @param {number} [options.maxWidth=1920] - Maximum width constraint
         * @param {number} [options.maxHeight=1080] - Maximum height constraint
         * @param {number} [options.quality=0.82] - Compression quality (0.0 - 1.0)
         * @returns {Promise<File>} - Resolves with optimized File (or original on fallback)
         */
        compressAndConvertToWebP: async function (file, options) {
            if (!this.isOptimizable(file)) {
                return file;
            }

            const settings = Object.assign({
                maxWidth: 1920,
                maxHeight: 1080,
                quality: 0.82,
                targetMime: 'image/webp'
            }, options || {});

            try {
                let img;
                if (typeof window.createImageBitmap === 'function') {
                    img = await window.createImageBitmap(file);
                } else {
                    img = await new Promise((resolve, reject) => {
                        const image = new Image();
                        const url = URL.createObjectURL(file);
                        image.onload = () => {
                            URL.revokeObjectURL(url);
                            resolve(image);
                        };
                        image.onerror = (err) => {
                            URL.revokeObjectURL(url);
                            reject(err);
                        };
                        image.src = url;
                    });
                }

                let srcWidth = img.width;
                let srcHeight = img.height;

                let targetWidth = srcWidth;
                let targetHeight = srcHeight;

                if (srcWidth > settings.maxWidth || srcHeight > settings.maxHeight) {
                    const ratio = Math.min(settings.maxWidth / srcWidth, settings.maxHeight / srcHeight);
                    targetWidth = Math.max(1, Math.round(srcWidth * ratio));
                    targetHeight = Math.max(1, Math.round(srcHeight * ratio));
                }

                const canvas = document.createElement('canvas');
                canvas.width = targetWidth;
                canvas.height = targetHeight;
                const ctx = canvas.getContext('2d', { alpha: true });

                if (!ctx) {
                    return file;
                }

                ctx.imageSmoothingEnabled = true;
                ctx.imageSmoothingQuality = 'high';
                ctx.drawImage(img, 0, 0, targetWidth, targetHeight);

                // Clean up ImageBitmap if applicable
                if (img.close) img.close();

                const optimizedBlob = await new Promise((resolve) => {
                    canvas.toBlob((blob) => {
                        resolve(blob);
                    }, settings.targetMime, settings.quality);
                });

                if (!optimizedBlob) {
                    return file;
                }

                // If optimized blob happens to be larger (e.g. tiny thumbnail already compressed), keep original
                if (optimizedBlob.size >= file.size && !/\.(bmp|png)$/i.test(file.name)) {
                    return file;
                }

                const baseName = file.name.replace(/\.[^/.]+$/, "");
                const newFileName = `${baseName}.webp`;

                const optimizedFile = new File([optimizedBlob], newFileName, {
                    type: 'image/webp',
                    lastModified: Date.now()
                });

                const savedPct = ((1 - (optimizedFile.size / file.size)) * 100).toFixed(1);
                console.info(`[ImageOptimizer] Optimized ${file.name} (${(file.size / 1024).toFixed(1)} KB) -> ${newFileName} (${(optimizedFile.size / 1024).toFixed(1)} KB, ${savedPct}% saved)`);

                return optimizedFile;
            } catch (err) {
                console.warn('[ImageOptimizer] Client-side optimization fallback:', err);
                return file;
            }
        },

        /**
         * Optimizes an array or FileList of files.
         */
        optimizeFiles: async function (fileListOrArray, options) {
            if (!fileListOrArray || fileListOrArray.length === 0) return [];
            const results = [];
            for (let i = 0; i < fileListOrArray.length; i++) {
                const opt = await this.compressAndConvertToWebP(fileListOrArray[i], options);
                results.push(opt);
            }
            return results;
        }
    };

    window.ImageOptimizer = ImageOptimizer;
})(window);
