//export function safeRandomUUID(): string {
//    const c: Crypto | undefined = typeof crypto !== "undefined" ? crypto : undefined;

//    const randomUUID = (c as (Crypto & { randomUUID?: () => string }) | undefined)?.randomUUID;
//    if (typeof randomUUID === "function") return randomUUID();

//    if (c && typeof c.getRandomValues === "function") {
//        const bytes = new Uint8Array(16);
//        c.getRandomValues(bytes);
//        bytes[6] = (bytes[6] & 0x0f) | 0x40;
//        bytes[8] = (bytes[8] & 0x3f) | 0x80;

//        const hex = Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
//        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
//    }

//    return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`;
//}
export function safeRandomUUID(): string {
    const c: Crypto | undefined = typeof crypto !== "undefined" ? crypto : undefined;

    const randomUUID = (c as (Crypto & { randomUUID?: () => string }) | undefined)?.randomUUID;

    // ✅ keep correct 'this' by calling with .call(c)
    if (typeof randomUUID === "function" && c) return randomUUID.call(c);

    if (c && typeof c.getRandomValues === "function") {
        const bytes = new Uint8Array(16);
        c.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;

        const hex = Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }

    return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`;
}
