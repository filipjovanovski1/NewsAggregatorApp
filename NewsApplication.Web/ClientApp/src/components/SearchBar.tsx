import { useEffect, useState, type Ref } from "react";

interface Props {
    onSearch: (q: string) => void;
    inline?: boolean;
    actionRef?: Ref<HTMLButtonElement>;
    value?: string; // controlled text from parent (e.g., "Skopje")
}

export default function SearchBar({ onSearch, inline = false, actionRef, value }: Props) {
    const [q, setQ] = useState(value ?? "");

    // keep local state in sync with parent-controlled value
    useEffect(() => {
        if (typeof value === "string") setQ(value);
    }, [value]);

    const submit = (e: React.FormEvent) => {
        e.preventDefault();
        onSearch(q.trim());
    };

    return (
        <div className={`searchbar ${inline ? "searchbar--inline" : ""}`}>
            <form onSubmit={submit}>
                <input
                    type="search"
                    placeholder="Search a country or city..."
                    value={q}
                    onChange={e => setQ(e.target.value)}
                    onKeyDown={e => { if (e.key === 'Enter') submit(e); }}
                />
                <button ref={actionRef} type="submit" disabled={!q.trim()}>
                    Search
                </button>
            </form>
        </div>
    );
}
