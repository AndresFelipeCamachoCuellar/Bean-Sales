/* =====================================================================
   bean-media.js — Gestor de fotos de producto (Cloudinary)

   Flujo por archivo (uno a la vez, para que el progreso y los errores
   sean legibles):

     1. POST {ticketUrl}  -> nuestro backend firma un ticket
     2. POST {uploadUrl}  -> el archivo va DIRECTO a Cloudinary (XHR, con
                             barra de progreso real vía upload.onprogress)
     3. POST {registerUrl}-> le contamos a nuestro backend qué se subió
     4. El backend responde la galería COMPLETA y aquí se re-renderiza.

   El archivo NUNCA pasa por nuestro servidor (256 MB de RAM en el host).
   Ningún secreto vive aquí: el api_secret se queda en el backend y el
   api_key llega dentro del ticket, solo tras autorizar al usuario.

   Depende de jQuery (ya cargado por _Layout / _LayoutStore) para los
   POST a nuestros endpoints, y de XMLHttpRequest para la subida (jQuery
   no expone el progreso de subida).
   ===================================================================== */
(function () {
    'use strict';

    function init(root) {
        if (!root || root.getAttribute('data-bean-media-ready') === '1') { return; }
        root.setAttribute('data-bean-media-ready', '1');

        var cfg = {
            productId: root.getAttribute('data-product-id'),
            maxImages: parseInt(root.getAttribute('data-max-images'), 10) || 6,
            maxBytes: parseInt(root.getAttribute('data-max-bytes'), 10) || 5242880,
            minDimension: parseInt(root.getAttribute('data-min-dimension'), 10) || 0,
            formats: (root.getAttribute('data-formats') || 'jpg,jpeg,png,webp').split(','),
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
        var counter = root.querySelector('[data-bean-media-count]');
        var emptyBox = root.querySelector('[data-bean-media-empty]');

        // Estado: se sincroniza con lo que devuelve el servidor tras cada operación.
        var images = readInitialState(root);
        var busy = false;

        // ---------------------------------------------------------- utilidades

        function token() {
            var el = root.querySelector('input[name="__RequestVerificationToken"]');
            return el ? el.value : '';
        }

        function post(url, data) {
            return $.ajax({
                url: url,
                type: 'POST',
                headers: { 'RequestVerificationToken': token() },
                data: $.extend({ __RequestVerificationToken: token() }, data),
                // traditional: true -> los arreglos se envían como "order=a&order=b",
                // que es la forma que el binder de ASP.NET Core ata a List<Guid> sin
                // ambigüedad (con la notación "order[]" el binding es frágil).
                traditional: true
            });
        }

        function showError(message) {
            if (!errorBox) { return; }
            if (!message) {
                errorBox.style.display = 'none';
                errorBox.textContent = '';
                return;
            }
            errorBox.textContent = message;
            errorBox.style.display = '';
        }

        function setBusy(value) {
            busy = value;
            root.classList.toggle('is-busy', !!value);
        }

        function extensionOf(name) {
            var i = (name || '').lastIndexOf('.');
            return i < 0 ? '' : name.substring(i + 1).toLowerCase();
        }

        function prettyMb(bytes) {
            return (Math.round(bytes / 1024 / 1024 * 10) / 10).toString().replace('.', ',');
        }

        // ---------------------------------------------------------- render

        function readInitialState(el) {
            var raw = el.getAttribute('data-images');
            if (!raw) { return []; }
            try { return JSON.parse(raw) || []; } catch (e) { return []; }
        }

        function render() {
            if (!grid) { return; }

            grid.innerHTML = '';

            images.forEach(function (img, index) {
                grid.appendChild(buildCard(img, index));
            });

            if (counter) {
                counter.textContent = images.length + ' de ' + cfg.maxImages;
            }

            if (emptyBox) {
                emptyBox.style.display = images.length === 0 ? '' : 'none';
            }

            if (drop) {
                var full = images.length >= cfg.maxImages;
                drop.classList.toggle('is-full', full);
                var hint = drop.querySelector('[data-bean-media-fullhint]');
                if (hint) { hint.style.display = full ? '' : 'none'; }
                var pick = drop.querySelector('[data-bean-media-pickhint]');
                if (pick) { pick.style.display = full ? 'none' : ''; }
            }
        }

        function buildCard(img, index) {
            var card = document.createElement('div');
            card.className = 'bean-media-item' + (img.isCover ? ' is-cover' : '');
            card.setAttribute('data-image-id', img.id);

            if (img.thumbUrl) {
                card.style.backgroundImage = "url('" + img.thumbUrl + "')";
            }

            if (img.isCover) {
                var badge = document.createElement('span');
                badge.className = 'bean-media-cover-badge';
                badge.textContent = 'Portada';
                card.appendChild(badge);
            }

            var actions = document.createElement('div');
            actions.className = 'bean-media-actions';

            if (!img.isCover) {
                actions.appendChild(actionButton('★', 'Marcar como portada', function () {
                    mutate(cfg.coverUrl, { productId: cfg.productId, imageId: img.id });
                }));
            }

            actions.appendChild(actionButton('←', 'Mover a la izquierda', function () {
                move(index, index - 1);
            }, index === 0));

            actions.appendChild(actionButton('→', 'Mover a la derecha', function () {
                move(index, index + 1);
            }, index === images.length - 1));

            actions.appendChild(actionButton('🗑', 'Eliminar foto', function () {
                if (!confirm('¿Eliminar esta foto del lote? No se puede deshacer.')) { return; }
                mutate(cfg.deleteUrl, { productId: cfg.productId, imageId: img.id });
            }, false, 'danger'));

            card.appendChild(actions);

            var foot = document.createElement('div');
            foot.className = 'bean-media-foot';

            var alt = document.createElement('input');
            alt.type = 'text';
            alt.className = 'bean-media-alt';
            alt.placeholder = 'Texto alternativo (opcional)';
            alt.maxLength = 200;
            alt.value = img.altText || '';
            alt.addEventListener('blur', function () {
                if ((img.altText || '') === alt.value) { return; }
                img.altText = alt.value;
                post(cfg.altUrl, { productId: cfg.productId, imageId: img.id, altText: alt.value });
            });
            foot.appendChild(alt);

            var meta = document.createElement('span');
            meta.className = 'bean-media-meta';
            meta.textContent = [img.dimensions, img.uploadedByLabel].filter(Boolean).join(' · ');
            foot.appendChild(meta);

            card.appendChild(foot);
            return card;
        }

        function actionButton(label, title, handler, disabled, extraClass) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'bean-media-act' + (extraClass ? ' ' + extraClass : '');
            b.title = title;
            b.setAttribute('aria-label', title);
            b.textContent = label;
            if (disabled) {
                b.disabled = true;
            } else {
                b.addEventListener('click', function (e) {
                    e.preventDefault();
                    if (busy) { return; }
                    handler();
                });
            }
            return b;
        }

        function move(from, to) {
            if (to < 0 || to >= images.length) { return; }
            var order = images.map(function (i) { return i.id; });
            var moved = order.splice(from, 1)[0];
            order.splice(to, 0, moved);
            mutate(cfg.reorderUrl, { productId: cfg.productId, order: order });
        }

        // ---------------------------------------------------------- mutaciones

        function mutate(url, data) {
            if (!url || busy) { return; }
            setBusy(true);
            showError(null);

            post(url, data)
                .done(function (res) {
                    if (!res || !res.ok) {
                        // Solo se reemplaza el estado en caso de ÉXITO: una respuesta de
                        // error trae la lista vacía y borraría la galería de la pantalla.
                        showError((res && res.error) || 'No pudimos aplicar el cambio. Inténtalo de nuevo.');
                        return;
                    }
                    if (res.images) {
                        images = res.images;
                        render();
                    }
                })
                .fail(function () {
                    showError('No pudimos conectarnos. Revisa tu conexión e inténtalo de nuevo.');
                })
                .always(function () { setBusy(false); });
        }

        // ---------------------------------------------------------- subida

        function validateLocally(file) {
            var ext = extensionOf(file.name);
            if (cfg.formats.indexOf(ext) === -1) {
                return 'Solo aceptamos ' + cfg.formatsLabel + '. "' + file.name + '" no sirve.';
            }
            if (file.size > cfg.maxBytes) {
                return 'La foto pesa ' + prettyMb(file.size) + ' MB y el máximo es ' + prettyMb(cfg.maxBytes) + ' MB.';
            }
            return null;
        }

        function queue(files) {
            var list = Array.prototype.slice.call(files || []);
            if (list.length === 0) { return; }

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

            uploadNext(list, 0);
        }

        function uploadNext(list, index) {
            if (index >= list.length) { return; }

            var file = list[index];
            var localError = validateLocally(file);

            if (localError) {
                showError(localError);
                uploadNext(list, index + 1);
                return;
            }

            uploadOne(file, function () {
                uploadNext(list, index + 1);
            });
        }

        function uploadOne(file, done) {
            setBusy(true);

            var ghost = document.createElement('div');
            ghost.className = 'bean-media-item is-uploading';
            var bar = document.createElement('span');
            bar.className = 'bean-media-progress';
            var fill = document.createElement('i');
            bar.appendChild(fill);
            ghost.appendChild(bar);
            if (grid) { grid.appendChild(ghost); }

            // Orden IMPORTANTE: primero se re-renderiza la galería (lo que elimina la
            // tarjeta fantasma) y solo al final se llama a done(), que puede arrancar la
            // subida del siguiente archivo y añadir su propio fantasma. Al revés, el
            // render borraría el fantasma recién creado.
            function cleanup() {
                if (ghost.parentNode) { ghost.parentNode.removeChild(ghost); }
                render();
                setBusy(false);
                done();
            }

            // 1. Ticket firmado.
            post(cfg.ticketUrl, { productId: cfg.productId })
                .done(function (ticket) {
                    if (!ticket || !ticket.ok) {
                        showError((ticket && ticket.error) || 'No pudimos preparar la subida.');
                        cleanup();
                        return;
                    }

                    // 2. Subida DIRECTA a Cloudinary.
                    var form = new FormData();
                    form.append('file', file);
                    form.append('api_key', ticket.apiKey);
                    form.append('timestamp', ticket.timestamp);
                    form.append('signature', ticket.signature);
                    form.append('public_id', ticket.publicId);
                    form.append('allowed_formats', ticket.allowedFormats);
                    if (ticket.uploadPreset) {
                        form.append('upload_preset', ticket.uploadPreset);
                    }

                    var xhr = new XMLHttpRequest();
                    xhr.open('POST', ticket.uploadUrl, true);

                    xhr.upload.onprogress = function (e) {
                        if (e.lengthComputable) {
                            fill.style.width = Math.round(e.loaded / e.total * 100) + '%';
                        }
                    };

                    xhr.onerror = function () {
                        showError('No pudimos subir la foto. Revisa tu conexión e inténtalo de nuevo.');
                        cleanup();
                    };

                    xhr.onload = function () {
                        var payload = null;
                        try { payload = JSON.parse(xhr.responseText); } catch (e) { payload = null; }

                        if (xhr.status < 200 || xhr.status >= 300 || !payload || !payload.public_id) {
                            var msg = payload && payload.error && payload.error.message
                                ? payload.error.message
                                : 'No pudimos subir la foto. Inténtalo de nuevo.';
                            showError(msg);
                            cleanup();
                            return;
                        }

                        // 3. Registro en nuestra BD.
                        post(cfg.registerUrl, {
                            productId: cfg.productId,
                            publicId: payload.public_id,
                            version: payload.version,
                            format: payload.format,
                            width: payload.width,
                            height: payload.height,
                            bytes: payload.bytes,
                            secureUrl: payload.secure_url,
                            signature: payload.signature
                        })
                            .done(function (res) {
                                if (!res || !res.ok) {
                                    showError((res && res.error) || 'La foto se subió pero no pudimos guardarla.');
                                } else if (res.images) {
                                    images = res.images;
                                }
                                cleanup();
                            })
                            .fail(function () {
                                showError('La foto se subió pero no pudimos guardarla. Vuelve a intentarlo.');
                                cleanup();
                            });
                    };

                    xhr.send(form);
                })
                .fail(function () {
                    showError('No pudimos preparar la subida. Revisa tu conexión.');
                    cleanup();
                });
        }

        // ---------------------------------------------------------- eventos

        if (input) {
            input.addEventListener('change', function () {
                queue(input.files);
                input.value = '';
            });
        }

        if (drop) {
            drop.addEventListener('click', function (e) {
                if (e.target && e.target.tagName === 'INPUT') { return; }
                if (images.length >= cfg.maxImages) {
                    showError('Llegaste al máximo de ' + cfg.maxImages + ' fotos. Elimina una para subir otra.');
                    return;
                }
                if (input) { input.click(); }
            });

            ['dragenter', 'dragover'].forEach(function (evt) {
                drop.addEventListener(evt, function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    drop.classList.add('is-dragover');
                });
            });

            ['dragleave', 'drop'].forEach(function (evt) {
                drop.addEventListener(evt, function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    drop.classList.remove('is-dragover');
                });
            });

            drop.addEventListener('drop', function (e) {
                if (e.dataTransfer && e.dataTransfer.files) {
                    queue(e.dataTransfer.files);
                }
            });
        }

        render();
    }

    document.addEventListener('DOMContentLoaded', function () {
        var nodes = document.querySelectorAll('[data-bean-media]');
        Array.prototype.forEach.call(nodes, init);
    });
})();
