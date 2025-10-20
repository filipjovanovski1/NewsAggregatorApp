// SearchBar.tsx
import { useState, Ref } from "react";

interface Props {
    onSearch: (q: string) => void;
    inline?: boolean;
    actionRef?: Ref<HTMLButtonElement>; // NEW: ref to the "Search" button
}

export default function SearchBar({ onSearch, inline = false, actionRef }: Props) {
    const [q, setQ] = useState("");

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
                />
                <button ref={actionRef} type="submit">Search</button>
            </form>
        </div>
    );
}
