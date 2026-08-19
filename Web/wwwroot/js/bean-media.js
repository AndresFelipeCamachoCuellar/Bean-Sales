/* Gestor de fotos de producto: backend form-urlencoded, Cloudinary multipart directo. */
(function () {
    'use strict';

    var icons = {
        cover: '<svg class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9L12 3Z" /></svg>',
        left: '<svg class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M19 12H5m6 6-6-6 6-6" /></svg>',
        right: '<svg class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M5 12h14m-6-6 6 6-6 6" /></svg>',
        delete: '<svg class="size-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M7 7l1 13h8l1-13M10 11v5M14 11v5" /></svg>'
    };

    function init(root) {
        if (!root || root.getAttribute('data-bean-media-ready') === '1') return;
        root.setAttribute('data-bean-media-ready', '1');

        var cfg = {
            productId: root.getAttribute('data-product-id'),
            maxImages: parseInt(root.getAttribute('data-max-images'), 10) || 6,
            maxBytes: parseInt(root.getAttribute('data-max-bytes'), 10) || 5242880,
            minDimension: parseInt(root.getAttribute('data-min-dimension'), 10) || 0,
            formats: (root.getAttribute('data-formats') || 'jpg,jpeg,png,webp').split(',').map(function (format) { return format.trim().toLowerCase(); }),
            formatsLabel: root.getAttribute('data-formats-label') || 'JPG, PNG o WebP',
            ticketUrl: root.getAttribute('data-ticket-url'),
            registerUrl: root.getAttribute('data-register-url'),
            deleteUrl: root.getAttribute('data-delete-url'),
            coverUrl: root.getAttribute('data-cover-url'),
            reorderUrl: root.getAttribute('data-reorder-url'),
            altUrl: root.getAttribute('data-alt-url')
        };

        var grid = root.querySelector('[data-bean-media-grid]');
        var drop = root.querySelector('[data-bean-media-drop]');
        var input = root.querySelector('[data-bean-media-input]');
        var errorBox = root.querySelector('[data-bean-media-error]');
        var statusBox = root.querySelector('[data-bean-media-status]');
        var counter = root.querySelector('[data-bean-media-count]');
        var emptyBox = root.querySelector('[data-bean-media-empty]');
        var images = readInitialState(root);
        var busy = false;

        function token() {
            var element = root.querySelector('input[name="__RequestVerificationToken"]');
            return element ? element.value : '';
        }

        async function postForm(url, data) {
            if (!url) throw new Error('La operación no está disponible.');

            var verificationToken = token();
            var body = new URLSearchParams();
            body.append('__RequestVerificationToken', verificationToken);

            Object.keys(data || {}).forEach(function (key) {
                var value = data[key];
                if (Array.isArray(value)) {
                    value.forEach(function (item) { body.append(key, item == null ? '' : String(item)); });
                } else {
                    body.append(key, value == null ? '' : String(value));
                }
            });

            var response = await fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                    'RequestVerificationToken': verificationToken,
                    'X-Requested-With': 'XMLHttpRequest'
                },
                body: body.toString()
            });

            var payload = null;
            try {
                payload = await response.json();
            } catch (error) {
                payload = null;
            }

            if (!response.ok) {
                throw new Error((payload && payload.error) || 'El servidor no pudo completar la operación.');
            }
            return payload;
        }

        function showError(message) {
            if (!errorBox) return;
            errorBox.textContent = message || '';
            errorBox.hidden = !message;
        }

        function announce(message) {
            if (!statusBox) return;
            statusBox.textContent = '';
            window.setTimeout(function () { statusBox.textContent = message || ''; }, 20);
        }

        function setBusy(value) {
            busy = !!value;
            root.setAttribute('aria-busy', busy ? 'true' : 'false');
            root.querySelectorAll('[data-bean-media-action], [data-bean-media-alt]').forEach(function (control) {
                control.disabled = busy || control.getAttribute('data-always-disabled') === 'true';
            });
            syncDropState();
        }

        function syncDropState() {
            var full = images.length >= cfg.maxImages;
            if (drop) {
                drop.setAttribute('aria-disabled', full || busy ? 'true' : 'false');
                drop.classList.toggle('border-clay', !full && !busy);
                drop.classList.toggle('opacity-60', full || busy);
            }
            if (input) input.disabled = full || busy;

            var fullHint = drop && drop.querySelector('[data-bean-media-fullhint]');
            var pickHint = drop && drop.querySelector('[data-bean-media-pickhint]');
            if (fullHint) fullHint.hidden = !full;
            if (pickHint) pickHint.hidden = full;
        }

        function extensionOf(name) {
            var index = (name || '').lastIndexOf('.');
            return index < 0 ? '' : name.substring(index + 1).toLowerCase();
        }

        function prettyMb(bytes) {
            return (Math.round(bytes / 1024 / 1024 * 10) / 10).toString().replace('.', ',');
        }

        function readInitialState(element) {
            var raw = element.getAttribute('data-images');
            if (!raw) return [];
            try {
                var parsed = JSON.parse(raw);
                return Array.isArray(parsed) ? parsed : [];
            } catch (error) {
                return [];
            }
        }

        function render() {
            if (!grid) return;
            grid.replaceChildren();
            images.forEach(function (image, index) { grid.appendChild(buildCard(image, index)); });
            if (counter) counter.textContent = images.length + ' de ' + cfg.maxImages;
            if (emptyBox) emptyBox.hidden = images.length !== 0;
            syncDropState();
        }

        function buildCard(image, index) {
            var card = document.createElement('article');
            card.className = 'overflow-hidden border border-ink/15 bg-cream';
            card.setAttribute('data-image-id', image.id);

            var visual = document.createElement('div');
            visual.className = 'relative aspect-[4/3] overflow-hidden bg-oat';

            var picture = document.createElement('img');
            picture.src = image.thumbUrl || '';
            picture.alt = image.altText || '';
            picture.className = 'size-full object-cover';

            if (image.fullUrl) {
                var fullLink = document.createElement('a');
                fullLink.href = image.fullUrl;
                fullLink.target = '_blank';
                fullLink.rel = 'noopener noreferrer';
                fullLink.className = 'block size-full cursor-zoom-in focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-clay';
                fullLink.setAttribute('aria-label', 'Abrir foto en tamaño completo');
                fullLink.appendChild(picture);
                visual.appendChild(fullLink);
            } else {
                visual.appendChild(picture);
            }

            if (image.isCover) {
                var badge = document.createElement('span');
                badge.className = 'absolute left-3 top-3 rounded-full bg-ink px-3 py-1.5 font-data text-[10px] font-semibold uppercase tracking-[0.1em] text-white';
                badge.textContent = 'Portada';
                visual.appendChild(badge);
            }
            card.appendChild(visual);

            var actions = document.createElement('div');
            actions.className = 'flex flex-wrap gap-2 border-b border-oat bg-white p-3';

            if (!image.isCover) {
                actions.appendChild(actionButton('cover', 'Marcar como portada', function () {
                    mutate(cfg.coverUrl, { productId: cfg.productId, imageId: image.id }, 'Portada actualizada.');
                }));
            }
            actions.appendChild(actionButton('left', 'Mover a la izquierda', function () { move(index, index - 1); }, index === 0));
            actions.appendChild(actionButton('right', 'Mover a la derecha', function () { move(index, index + 1); }, index === images.length - 1));
            actions.appendChild(actionButton('delete', 'Eliminar foto', function () {
                if (window.confirm('¿Eliminar esta foto del lote? No se puede deshacer.')) {
                    mutate(cfg.deleteUrl, { productId: cfg.productId, imageId: image.id }, 'Foto eliminada.');
                }
            }, false, true));
            card.appendChild(actions);

            var details = document.createElement('div');
            details.className = 'p-3';

            var altLabel = document.createElement('label');
            altLabel.className = 'font-data text-[10px] font-semibold uppercase tracking-[0.12em] text-ink/55';
            altLabel.htmlFor = 'bean-alt-' + image.id;
            altLabel.textContent = 'Texto alternativo';
            details.appendChild(altLabel);

            var alt = document.createElement('input');
            alt.id = 'bean-alt-' + image.id;
            alt.type = 'text';
            alt.className = 'mt-2 min-h-11 w-full border border-ink/20 bg-white px-3 py-2 text-sm text-ink caret-clay outline-none transition-colors placeholder:text-ink/40 hover:border-ink/40 focus-visible:border-clay focus-visible:ring-2 focus-visible:ring-clay/25 disabled:cursor-wait disabled:opacity-60';
            alt.placeholder = 'Describe la foto (opcional)';
            alt.maxLength = 200;
            alt.value = image.altText || '';
            alt.setAttribute('data-bean-media-alt', '');
            alt.addEventListener('keydown', function (event) {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    alt.blur();
                }
            });
            alt.addEventListener('blur', function () { saveAlt(image, alt); });
            details.appendChild(alt);

            var meta = document.createElement('p');
            meta.className = 'mt-2 text-xs font-semibold leading-5 text-ink/50';
            meta.textContent = [image.dimensions, image.uploadedByLabel, image.uploadedOn].filter(Boolean).join(' · ');
            details.appendChild(meta);
            card.appendChild(details);
            return card;
        }

        function actionButton(icon, title, handler, disabled, danger) {
            var button = document.createElement('button');
            button.type = 'button';
            button.className = danger
                ? 'inline-grid size-11 cursor-pointer place-items-center border border-clay/30 text-clay-dark transition-colors hover:bg-clay hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-clay disabled:cursor-not-allowed disabled:opacity-35'
                : 'inline-grid size-11 cursor-pointer place-items-center border border-ink/20 text-ink transition-colors hover:border-clay hover:bg-clay hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-clay disabled:cursor-not-allowed disabled:opacity-35';
            button.title = title;
            button.setAttribute('aria-label', title);
            button.setAttribute('data-bean-media-action', '');
            button.setAttribute('data-always-disabled', disabled ? 'true' : 'false');
            button.disabled = !!disabled;
            button.innerHTML = icons[icon];
            if (!disabled) {
                button.addEventListener('click', function () {
                    if (!busy) handler();
                });
            }
            return button;
        }

        function move(from, to) {
            if (to < 0 || to >= images.length) return;
            var order = images.map(function (image) { return image.id; });
            var moved = order.splice(from, 1)[0];
            order.splice(to, 0, moved);
            mutate(cfg.reorderUrl, { productId: cfg.productId, order: order }, 'Orden de fotos actualizado.');
        }

        async function mutate(url, data, successMessage) {
            if (!url || busy) return;
            setBusy(true);
            showError(null);
            try {
                var response = await postForm(url, data);
                if (!response || !response.ok) {
                    showError((response && response.error) || 'No pudimos aplicar el cambio. Inténtalo de nuevo.');
                    return;
                }
                if (response.images) {
                    images = response.images;
                    render();
                }
                announce(successMessage);
            } catch (error) {
                showError(error.message || 'No pudimos conectarnos. Revisa tu conexión e inténtalo de nuevo.');
            } finally {
                setBusy(false);
            }
        }

        async function saveAlt(image, field) {
            var previous = image.altText || '';
            if (busy || previous === field.value) return;
            setBusy(true);
            showError(null);
            try {
                var response = await postForm(cfg.altUrl, {
                    productId: cfg.productId,
                    imageId: image.id,
                    altText: field.value
                });
                if (!response || !response.ok) {
                    field.value = previous;
                    showError((response && response.error) || 'No pudimos guardar el texto alternativo.');
                    return;
                }
                if (response.images) {
                    images = response.images;
                    render();
                } else {
                    image.altText = field.value;
                }
                announce('Texto alternativo guardado.');
            } catch (error) {
                field.value = previous;
                showError(error.message || 'No pudimos guardar el texto alternativo.');
            } finally {
                setBusy(false);
            }
        }

        function validateFile(file) {
            var extension = extensionOf(file.name);
            if (cfg.formats.indexOf(extension) === -1) {
                return 'Solo aceptamos ' + cfg.formatsLabel + '. "' + file.name + '" no es compatible.';
            }
            if (file.size > cfg.maxBytes) {
                return 'La foto pesa ' + prettyMb(file.size) + ' MB y el máximo es ' + prettyMb(cfg.maxBytes) + ' MB.';
            }
            return null;
        }

        function validateDimensions(file) {
            if (cfg.minDimension <= 0) return Promise.resolve(null);
            return new Promise(function (resolve) {
                var objectUrl = URL.createObjectURL(file);
                var preview = new Image();
                preview.onload = function () {
                    URL.revokeObjectURL(objectUrl);
                    if (preview.naturalWidth < cfg.minDimension || preview.naturalHeight < cfg.minDimension) {
                        resolve('La foto "' + file.name + '" mide ' + preview.naturalWidth + '×' + preview.naturalHeight + ' px. El mínimo es ' + cfg.minDimension + '×' + cfg.minDimension + ' px.');
                    } else {
                        resolve(null);
                    }
                };
                preview.onerror = function () {
                    URL.revokeObjectURL(objectUrl);
                    resolve('No pudimos leer la foto "' + file.name + '". Verifica que el archivo no esté dañado.');
                };
                preview.src = objectUrl;
            });
        }

        async function queue(files) {
            if (busy) return;
            var list = Array.prototype.slice.call(files || []);
            if (!list.length) return;

            var room = cfg.maxImages - images.length;
            if (room <= 0) {
                showError('Llegaste al máximo de ' + cfg.maxImages + ' fotos. Elimina una para subir otra.');
                return;
            }
            if (list.length > room) {
                showError('Solo caben ' + room + ' foto(s) más; se subirán las primeras.');
                list = list.slice(0, room);
            } else {
                showError(null);
            }

            for (var index = 0; index < list.length; index++) {
                var file = list[index];
                var localError = validateFile(file) || await validateDimensions(file);
                if (localError) {
                    showError(localError);
                    continue;
                }
                await uploadOne(file);
            }
        }

        function createUploadCard(file) {
            var card = document.createElement('div');
            card.className = 'flex min-h-48 flex-col justify-end border border-clay/35 bg-ink p-4 text-white';
            card.setAttribute('role', 'progressbar');
            card.setAttribute('aria-label', 'Subiendo ' + file.name);
            card.setAttribute('aria-valuemin', '0');
            card.setAttribute('aria-valuemax', '100');
            card.setAttribute('aria-valuenow', '0');

            var name = document.createElement('p');
            name.className = 'truncate text-sm font-extrabold';
            name.textContent = file.name;
            card.appendChild(name);

            var track = document.createElement('div');
            track.className = 'mt-3 h-2 overflow-hidden bg-white/15';
            var fill = document.createElement('div');
            fill.className = 'h-full w-0 bg-clay-light transition-[width] duration-150';
            track.appendChild(fill);
            card.appendChild(track);
            return { card: card, fill: fill };
        }

        function uploadToCloudinary(ticket, file, progress) {
            return new Promise(function (resolve, reject) {
                var form = new FormData();
                form.append('file', file);
                form.append('api_key', ticket.apiKey);
                form.append('timestamp', ticket.timestamp);
                form.append('signature', ticket.signature);
                form.append('public_id', ticket.publicId);
                form.append('allowed_formats', ticket.allowedFormats);
                if (ticket.uploadPreset) form.append('upload_preset', ticket.uploadPreset);

                var xhr = new XMLHttpRequest();
                xhr.open('POST', ticket.uploadUrl, true);
                xhr.upload.onprogress = function (event) {
                    if (!event.lengthComputable) return;
                    var percent = Math.round(event.loaded / event.total * 100);
                    progress.fill.style.width = percent + '%';
                    progress.card.setAttribute('aria-valuenow', String(percent));
                };
                xhr.onerror = function () { reject(new Error('No pudimos subir la foto. Revisa tu conexión e inténtalo de nuevo.')); };
                xhr.onload = function () {
                    var payload = null;
                    try { payload = JSON.parse(xhr.responseText); } catch (error) { payload = null; }
                    if (xhr.status < 200 || xhr.status >= 300 || !payload || !payload.public_id) {
                        reject(new Error(payload && payload.error && payload.error.message ? payload.error.message : 'No pudimos subir la foto. Inténtalo de nuevo.'));
                        return;
                    }
                    resolve(payload);
                };
                xhr.send(form);
            });
        }

        async function uploadOne(file) {
            setBusy(true);
            showError(null);
            var progress = createUploadCard(file);
            if (grid) grid.appendChild(progress.card);

            try {
                var ticket = await postForm(cfg.ticketUrl, { productId: cfg.productId });
                if (!ticket || !ticket.ok) throw new Error((ticket && ticket.error) || 'No pudimos preparar la subida.');

                var uploaded = await uploadToCloudinary(ticket, file, progress);
                var response = await postForm(cfg.registerUrl, {
                    productId: cfg.productId,
                    publicId: uploaded.public_id,
                    version: uploaded.version,
                    format: uploaded.format,
                    width: uploaded.width,
                    height: uploaded.height,
                    bytes: uploaded.bytes,
                    secureUrl: uploaded.secure_url,
                    signature: uploaded.signature
                });

                if (!response || !response.ok) throw new Error((response && response.error) || 'La foto se subió pero no pudimos guardarla.');
                if (response.images) images = response.images;
                announce('Foto subida y registrada.');
            } catch (error) {
                showError(error.message || 'No pudimos completar la subida. Inténtalo de nuevo.');
            } finally {
                if (progress.card.parentNode) progress.card.parentNode.removeChild(progress.card);
                render();
                setBusy(false);
            }
        }

        if (input) {
            input.addEventListener('change', function () {
                queue(input.files);
                input.value = '';
            });
        }

        if (drop) {
            drop.addEventListener('click', function (event) {
                if (event.target && event.target.tagName === 'INPUT') return;
                if (images.length >= cfg.maxImages || busy) {
                    if (images.length >= cfg.maxImages) showError('Llegaste al máximo de ' + cfg.maxImages + ' fotos. Elimina una para subir otra.');
                    return;
                }
                if (input) input.click();
            });
            drop.addEventListener('keydown', function (event) {
                if (event.key !== 'Enter' && event.key !== ' ') return;
                event.preventDefault();
                if (!busy && images.length < cfg.maxImages && input) input.click();
            });
            ['dragenter', 'dragover'].forEach(function (eventName) {
                drop.addEventListener(eventName, function (event) {
                    event.preventDefault();
                    event.stopPropagation();
                    if (!busy && images.length < cfg.maxImages) {
                        drop.classList.add('border-clay', 'bg-oat');
                    }
                });
            });
            ['dragleave', 'drop'].forEach(function (eventName) {
                drop.addEventListener(eventName, function (event) {
                    event.preventDefault();
                    event.stopPropagation();
                    drop.classList.remove('bg-oat');
                });
            });
            drop.addEventListener('drop', function (event) {
                if (event.dataTransfer && event.dataTransfer.files) queue(event.dataTransfer.files);
            });
        }

        render();
    }

    function initializeAll() {
        document.querySelectorAll('[data-bean-media]').forEach(init);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeAll);
    } else {
        initializeAll();
    }
})();
