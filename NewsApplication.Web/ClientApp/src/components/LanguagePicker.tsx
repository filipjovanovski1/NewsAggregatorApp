import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { ChevronDown } from 'lucide-react';
import { ARTICLE_LANGUAGES } from '../articleLanguages';
import FlagIcon from './FlagIcon';

interface Props {
    value: string;
    onChange: (language: string) => void;
}

export default function LanguagePicker({ value, onChange }: Props) {
    const [open, setOpen] = useState(false);
    const pickerRef = useRef<HTMLDivElement>(null);
    const listRef = useRef<HTMLDivElement>(null);
    const selected = ARTICLE_LANGUAGES.find(language => language.code === value)
        ?? ARTICLE_LANGUAGES[ARTICLE_LANGUAGES.length - 1];
    const orderedLanguages = useMemo(
        () => [selected, ...ARTICLE_LANGUAGES.filter(language => language.code !== selected.code)],
        [selected]
    );

    useEffect(() => {
        if (!open) return;

        const closeOnOutsidePointer = (event: PointerEvent) => {
            if (!pickerRef.current?.contains(event.target as Node)) setOpen(false);
        };
        document.addEventListener('pointerdown', closeOnOutsidePointer);
        return () => document.removeEventListener('pointerdown', closeOnOutsidePointer);
    }, [open]);

    useEffect(() => {
        if (!open) return;
        listRef.current?.scrollTo({ top: 0 });
        listRef.current?.querySelector<HTMLButtonElement>('[role="option"]')?.focus();
    }, [open]);

    const handleOptionKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
        const options = Array.from(
            listRef.current?.querySelectorAll<HTMLButtonElement>('[role="option"]') ?? []
        );
        const currentIndex = options.indexOf(event.currentTarget);
        let nextIndex = currentIndex;

        if (event.key === 'ArrowDown') nextIndex = Math.min(options.length - 1, currentIndex + 1);
        else if (event.key === 'ArrowUp') nextIndex = Math.max(0, currentIndex - 1);
        else if (event.key === 'Home') nextIndex = 0;
        else if (event.key === 'End') nextIndex = options.length - 1;
        else if (event.key === 'Escape') {
            setOpen(false);
            pickerRef.current?.querySelector<HTMLButtonElement>('.topbar-language-trigger')?.focus();
            return;
        } else return;

        event.preventDefault();
        options[nextIndex]?.focus();
    };

    return (
        <div className="topbar-language-picker" ref={pickerRef}>
            <button
                type="button"
                className="topbar-language-trigger"
                aria-label={`Article language: ${selected.label}`}
                aria-haspopup="listbox"
                aria-expanded={open}
                onClick={() => setOpen(current => !current)}
                onKeyDown={(event) => {
                    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                        event.preventDefault();
                        setOpen(true);
                    }
                }}
            >
                <FlagIcon country={selected.country} />
                <span>{selected.label}</span>
                <ChevronDown className={open ? 'is-open' : ''} size={15} aria-hidden />
            </button>

            {open && (
                <div
                    className="topbar-language-menu"
                    ref={listRef}
                    role="listbox"
                    aria-label="Article language"
                >
                    {orderedLanguages.map(language => (
                        <button
                            type="button"
                            role="option"
                            aria-selected={language.code === selected.code}
                            className="topbar-language-option"
                            key={language.code}
                            onClick={() => {
                                onChange(language.code);
                                setOpen(false);
                            }}
                            onKeyDown={handleOptionKeyDown}
                        >
                            <FlagIcon country={language.country} />
                            <span>{language.label}</span>
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}
