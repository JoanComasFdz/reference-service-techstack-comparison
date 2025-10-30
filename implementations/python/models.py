import os
from sqlalchemy import Column, Integer, String, DateTime, Index, create_engine
from sqlalchemy.orm import declarative_base, sessionmaker

Base = declarative_base()

class InstrumentStatus(Base):
    __tablename__ = 'python_instrument_status'

    id = Column(Integer, primary_key=True, autoincrement=True)
    device_id = Column(String(255), nullable=False)
    previous_status = Column(String(255), nullable=False)
    current_status = Column(String(255), nullable=False)
    timestamp = Column(DateTime(timezone=True), nullable=False)

    __table_args__ = (
        Index('idx_python_instrument_status_timestamp', 'timestamp', postgresql_ops={'timestamp': 'DESC'}),
    )

# Database configuration from environment variables
DB_HOST = os.getenv('POSTGRES_HOST', 'localhost')
DB_PORT = int(os.getenv('POSTGRES_PORT', '5432'))
DB_NAME = os.getenv('POSTGRES_DB', 'python_db')
DB_USER = os.getenv('POSTGRES_USER', 'admin')
DB_PASSWORD = os.getenv('POSTGRES_PASSWORD', 'admin')

# Create database URL using psycopg3 driver (modern, Python 3.13 native)
DATABASE_URL = f"postgresql+psycopg://{DB_USER}:{DB_PASSWORD}@{DB_HOST}:{DB_PORT}/{DB_NAME}"

# Create engine and session factory
engine = create_engine(DATABASE_URL, pool_pre_ping=True)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)

def init_db() -> None:
    """Initialize database tables"""
    Base.metadata.create_all(bind=engine)
