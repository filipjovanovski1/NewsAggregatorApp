import { useEffect } from 'react';
import type { ArticleDto } from '../types';
import ArticleCard from './ArticleCard';

interface Props {
    items: ArticleDto[];
    total?: number;
    page: number;
    pageSize: number;
    onPrev: () => void;
    onNext: () => void;
    canPrev: boolean;
    canNext: boolean;
    onClose: () => void;
    title?: string;
}

export default function ArticleOverlay({
    items,
    total,
    page,
    pageSize,
    onPrev,
    onNext,
    canPrev,
    canNext,
    onClose,
    title
}: Props) {
    // keyboard: ← → Esc
    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'ArrowLeft' && canPrev) onPrev();
            if (e.key === 'ArrowRight' && canNext) onNext();
            if (e.key === 'Escape') onClose();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onPrev, onNext, onClose, canPrev, canNext]);

    const shown = items.slice(0, 6); // 2 rows × 3 columns
    const totalNum = total ?? items.length;
    const totalPages = Math.max(1, Math.ceil(totalNum / pageSize));

    return (
        <div className="overlay">
            <div className="overlay-inner">
                <div className="overlay-header">
                    <h3>{title ?? 'Articles'}</h3>
                    <div className="overlay-header-right">
                        <span className="count">Page {page}/{totalPages} · {totalNum} total</span>
                        <button className="close" onClick={onClose} aria-label="Close">×</button>
                    </div>
                </div>

                {/* ---- INLINE ARROWS + GRID ---- */}
                <div key={page} className="grid3x-wrap">
                    {/* Left side: show button only if canPrev, otherwise spacer to keep width */}
                    {canPrev ? (
                        <button className="arrow-side left" onClick={onPrev} aria-label="Previous page">‹</button>
                    ) : (
                        <span className="arrow-side-spacer" aria-hidden />
                    )}

                    <div className="grid3x">
                        {shown.map(a => <ArticleCard key={a.id} article={a} />)}
                    </div>

                    {/* Right side: show button only if canNext, otherwise spacer */}
                    {canNext ? (
                        <button className="arrow-side right" onClick={onNext} aria-label="Next page">›</button>
                    ) : (
                        <span className="arrow-side-spacer" aria-hidden />
                    )}
                </div>
            </div>
        </div>
    );
}
