# Costos de la pasarela de pagos — Wompi

**Fuente:** panel de registro de Wompi (comercios.wompi.co), consultado el **28-jul-2026**.
**Cuenta:** persona natural (Andrés Camacho) · Comercio: The Kng Bean · Clientes nacionales.
⚠️ Las tarifas están sujetas a cambios por parte de Wompi. Verificar antes de decisiones de precio importantes.

---

## 1. Tarifas vigentes

| Producto | Tarifa (por transacción exitosa) | ¿Lo usamos? |
|---|---|---|
| **Recibir pagos en línea** (e-commerce) | **2,65% + $700 + IVA** | ✅ Es el que integramos |
| Venta presencial (app de Wompi) | 1,98% + IVA | ❌ No aplica hoy |
| Pagos a terceros (desde el saldo Wompi) | $1.849 + 0,4% + IVA | ❌ Útil a futuro para pagar a proveedores |

### Medios de pago incluidos en "pagos en línea"
Tarjetas de crédito y débito · QR Bancolombia · Botón Bancolombia · Compra ahora y paga después (BNPL) · Corresponsal Bancario · **PSE** · **Nequi** · Daviplata · Su+Pay.

> Todos vienen incluidos en la misma tarifa: no hay que integrar cada método por separado ni pagar extra por habilitarlos.

---

## 2. Impacto real en el ticket (cálculo)

Fórmula: `comisión = (valor × 2,65%) + $700`, más **IVA del 19% sobre la comisión**.

| Valor cobrado al cliente | Comisión + IVA | % efectivo | Neto recibido |
|---|---|---|---|
| $30.000 | ~$1.784 | **5,9%** | $28.216 |
| $50.000 | ~$2.410 | **4,8%** | $47.590 |
| $80.000 | ~$3.348 | **4,2%** | $76.652 |
| $118.000 *(caso real: 2 bolsas + envío)* | ~$4.554 | **3,9%** | $113.446 |
| $200.000 | ~$7.140 | **3,6%** | $192.860 |
| $500.000 *(pedido mayorista)* | ~$16.610 | **3,3%** | $483.390 |

### Conclusiones para el negocio
1. **El costo fijo de $700 castiga los tickets pequeños.** A $30.000 la comisión efectiva es del 5,9%; a $200.000 baja al 3,6%. Subir el ticket promedio (packs de varios lotes, suscripción, mayoreo) mejora el margen sin tocar precios.
2. **Regla práctica de costeo:** provisionar **~4% del valor total** de cada venta (incluido el envío, porque la comisión se calcula sobre el total cobrado) para tickets medianos; ~5% si el ticket típico es bajo.
3. **Ojo con la consignación:** si la comisión de Bean al proveedor es del 20-30% y la pasarela se lleva ~4% del total cobrado, ese 4% debe descontarse de la parte de Bean, no de la del proveedor (salvo que se pacte lo contrario en el acuerdo con el proveedor). **Definir esto por escrito antes del primer proveedor real.**
4. La comisión se cobra **solo sobre transacciones exitosas**: los pagos rechazados no cuestan.

---

## 3. Pendientes relacionados
- Incluir estos porcentajes en el **modelo financiero** (sigue pendiente en el roadmap de negocio).
- Definir en el acuerdo con proveedores quién asume el costo de la pasarela en el esquema de consignación.
- Revisar si al formalizar (SAS) cambian las condiciones o se puede negociar una tarifa mejor por volumen.
