import type { ArticleDto } from '../types';

export default function ArticleCard({ article }: { article: ArticleDto }) {
    const date = article.publishedUtc ? new Date(article.publishedUtc) : null;
    const hasImg = (article as any).imageUrl; // optional in your DTO

    return (
        <a className="article-card" href={article.url} target="_blank" rel="noreferrer">
            {hasImg && (
                <div className="img" style={{ backgroundImage: `url(${(article as any).imageUrl})` }} aria-hidden />
            )}
            <div className="meta">
                <h4 title={article.title}>{article.title}</h4>
                <p className="snippet">{article.snippet ?? ''}</p>
                <div className="footer">
                    <span className="src">{article.sourceName}</span>
                    {date && <span className="time">{date.toLocaleString()}</span>}
                </div>
            </div>
        </a>
    );
}
